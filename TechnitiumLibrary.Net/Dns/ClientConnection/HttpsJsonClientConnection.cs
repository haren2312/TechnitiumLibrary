﻿/*
Technitium Library
Copyright (C) 2021  Shreyas Zare (shreyas@technitium.com)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

*/

using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TechnitiumLibrary.Net.Proxy;

namespace TechnitiumLibrary.Net.Dns.ClientConnection
{
    public class HttpsJsonClientConnection : DnsClientConnection
    {
        #region variables

        readonly HttpClient _httpClient;

        bool _pooled;
        DateTime _lastQueried;

        #endregion

        #region constructor

        public HttpsJsonClientConnection(NameServerAddress server, NetProxy proxy)
            : base(DnsTransportProtocol.HttpsJson, server, proxy)
        {
            if (proxy == null)
            {
                _httpClient = new HttpClient();
            }
            else
            {
                SocketsHttpHandler handler = new SocketsHttpHandler();
                handler.Proxy = proxy;

                _httpClient = new HttpClient(handler);
            }

            _httpClient.DefaultRequestHeaders.Add("accept", "application/dns-json");

            if (_server.DnsOverHttpEndPoint.IsDefaultPort)
                _httpClient.DefaultRequestHeaders.Add("host", _server.DnsOverHttpEndPoint.Host);
            else
                _httpClient.DefaultRequestHeaders.Add("host", _server.DnsOverHttpEndPoint.Host + ":" + _server.DnsOverHttpEndPoint.Port);

            _httpClient.DefaultRequestHeaders.Add("user-agent", "DoH client");
        }

        #endregion

        #region IDisposable

        bool _disposed;

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing && !_pooled)
            {
                if (_httpClient != null)
                    _httpClient.Dispose();
            }

            _disposed = true;
        }

        #endregion

        #region public

        public override async Task<DnsDatagram> QueryAsync(DnsDatagram request, int timeout, int retries, CancellationToken cancellationToken)
        {
            _lastQueried = DateTime.UtcNow;

            async Task<HttpRequestMessage> GetHttpRequest()
            {
                Uri queryUri;

                if (_proxy == null)
                {
                    if (_server.IsIPEndPointStale)
                        await _server.RecursiveResolveIPAddressAsync();

                    queryUri = new Uri(_server.DnsOverHttpEndPoint.Scheme + "://" + _server.IPEndPoint.ToString() + _server.DnsOverHttpEndPoint.PathAndQuery);
                }
                else
                {
                    if (_server.IPEndPoint == null)
                        queryUri = _server.DnsOverHttpEndPoint;
                    else
                        queryUri = new Uri(_server.DnsOverHttpEndPoint.Scheme + "://" + _server.IPEndPoint.ToString() + _server.DnsOverHttpEndPoint.PathAndQuery);
                }

                return new HttpRequestMessage(HttpMethod.Get, queryUri.AbsoluteUri + "?name=" + (request.Question[0].Name.Length == 0 ? "." : request.Question[0].Name) + "&type=" + Convert.ToString((int)request.Question[0].Type) + "&do=" + ((request.EDNS is not null) && request.EDNS.Flags.HasFlag(EDnsHeaderFlags.DNSSEC_OK)));
            }

            //DoH JSON format request 
            Stopwatch stopwatch = new Stopwatch();
            int retry = 0;
            while (retry < retries) //retry loop
            {
                retry++;

                if (cancellationToken.IsCancellationRequested)
                    return await Task.FromCanceled<DnsDatagram>(cancellationToken); //task cancelled

                stopwatch.Start();

                Task<HttpResponseMessage> task = _httpClient.SendAsync(await GetHttpRequest(), cancellationToken);

                using (CancellationTokenSource timeoutCancellationTokenSource = new CancellationTokenSource())
                {
                    using (CancellationTokenRegistration ctr = cancellationToken.Register(delegate () { timeoutCancellationTokenSource.Cancel(); }))
                    {
                        if (await Task.WhenAny(task, Task.Delay(timeout, timeoutCancellationTokenSource.Token)) != task)
                            continue; //request timed out; retry
                    }

                    timeoutCancellationTokenSource.Cancel(); //to stop delay task
                }

                HttpResponseMessage httpResponse = await task;

                stopwatch.Stop();
                httpResponse.EnsureSuccessStatusCode();

                string responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

                //parse response
                DnsDatagram response = DnsDatagram.ReadFromJson(JsonConvert.DeserializeObject(responseJson), responseJson.Length, request.EDNS);

                response.SetIdentifier(request.Identifier);
                response.SetMetadata(_server, _protocol, stopwatch.Elapsed.TotalMilliseconds);

                if (response.Question.Count != request.Question.Count)
                    throw new DnsClientResponseValidationException("Invalid response was received: question count mismatch.");

                for (int i = 0; i < response.Question.Count; i++)
                {
                    if (request.Question[i].ZoneCut == null)
                    {
                        if (!response.Question[i].Name.Equals(request.Question[i].Name, StringComparison.Ordinal))
                            throw new DnsClientResponseValidationException("Invalid response was received: QNAME mismatch.");

                        if (response.Question[i].Type != request.Question[i].Type)
                            throw new DnsClientResponseValidationException("Invalid response was received: QTYPE mismatch.");
                    }
                    else
                    {
                        if (!response.Question[i].Name.Equals(request.Question[i].MinimizedName, StringComparison.Ordinal))
                            throw new DnsClientResponseValidationException("Invalid response was received: QNAME mismatch.");

                        if (response.Question[i].Type != request.Question[i].MinimizedType)
                            throw new DnsClientResponseValidationException("Invalid response was received: QTYPE mismatch.");
                    }

                    if (response.Question[i].Class != request.Question[i].Class)
                        throw new DnsClientResponseValidationException("Invalid response was received: QCLASS mismatch.");
                }

                return response;
            }

            throw new DnsClientException("DnsClient failed to resolve the request: request timed out.");
        }

        #endregion

        #region properties

        internal DateTime LastQueried
        { get { return _lastQueried; } }

        internal bool Pooled
        {
            get { return _pooled; }
            set { _pooled = value; }
        }

        #endregion
    }
}

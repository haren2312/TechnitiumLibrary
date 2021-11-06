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

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using TechnitiumLibrary.IO;

namespace TechnitiumLibrary.Net.Dns.ResourceRecords
{
    [Flags]
    public enum DnsDnsKeyFlag : ushort
    {
        ZoneKey = 0x100,
        SecureEntryPoint = 0x1
    }

    public enum DnssecAlgorithm : byte
    {
        RSA_MD5 = 1,
        DSA_SHA1 = 3,
        RSA_SHA1 = 5,
        DSA_NSEC3_SHA1 = 6,
        RSASHA1_NSEC3_SHA1 = 7,
        RSA_SHA256 = 8,
        RSA_SHA512 = 10,
        ECC_GOST = 12,
        ECDSA_P256_SHA256 = 13,
        ECDSA_P384_SHA384 = 14,
        ED25519 = 15,
        ED448 = 16,
        PRIVATEDNS = 253,
        PRIVATEOID = 254
    }

    public class DnsDNSKEYRecord : DnsResourceRecordData
    {
        #region variables

        DnsDnsKeyFlag _flags;
        byte _protocol;
        DnssecAlgorithm _algorithm;
        DnssecPublicKey _publicKey;

        byte[] _serializedData;

        #endregion

        #region constructors

        public DnsDNSKEYRecord(DnsDnsKeyFlag flags, byte protocol, DnssecAlgorithm algorithm, DnssecPublicKey publicKey)
        {
            _flags = flags;
            _protocol = protocol;
            _algorithm = algorithm;
            _publicKey = publicKey;
        }

        public DnsDNSKEYRecord(Stream s)
            : base(s)
        { }

        public DnsDNSKEYRecord(dynamic jsonResourceRecord)
        {
            throw new NotSupportedException();
        }

        #endregion

        #region protected

        protected override void Parse(Stream s)
        {
            _serializedData = s.ReadBytes(_rdLength);

            using (MemoryStream mS = new MemoryStream(_serializedData))
            {
                _flags = (DnsDnsKeyFlag)DnsDatagram.ReadUInt16NetworkOrder(mS);
                _protocol = mS.ReadByteValue();
                _algorithm = (DnssecAlgorithm)mS.ReadByteValue();
                _publicKey = DnssecPublicKey.Parse(_algorithm, mS.ReadBytes(_rdLength - 2 - 1 - 1));
            }
        }

        protected override void WriteRecordData(Stream s, List<DnsDomainOffset> domainEntries)
        {
            if (_serializedData is null)
            {
                using (MemoryStream mS = new MemoryStream())
                {
                    DnsDatagram.WriteUInt16NetworkOrder((ushort)_flags, mS);
                    mS.WriteByte(_protocol);
                    mS.WriteByte((byte)_algorithm);
                    _publicKey.WriteTo(s);

                    _serializedData = mS.ToArray();
                }
            }

            s.Write(_serializedData);
        }

        #endregion

        #region public

        public override bool Equals(object obj)
        {
            if (obj is null)
                return false;

            if (ReferenceEquals(this, obj))
                return true;

            if (obj is DnsDNSKEYRecord other)
            {
                if (_flags != other._flags)
                    return false;

                if (_protocol != other._protocol)
                    return false;

                if (_algorithm != other._algorithm)
                    return false;

                if (!BinaryNumber.Equals(_publicKey, other._publicKey))
                    return false;

                return true;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_flags, _protocol, _algorithm, _publicKey);
        }

        public override string ToString()
        {
            return (ushort)_flags + " " + _protocol + " " + (byte)_algorithm + " ( " + Convert.ToBase64String(_publicKey.PublicKey) + " )";
        }

        #endregion

        #region properties

        public DnsDnsKeyFlag Flags
        { get { return _flags; } }

        public byte Protocol
        { get { return _protocol; } }

        public DnssecAlgorithm Algorithm
        { get { return _algorithm; } }

        public DnssecPublicKey PublicKey
        { get { return _publicKey; } }

        [IgnoreDataMember]
        public override ushort UncompressedLength
        { get { return Convert.ToUInt16(2 + 1 + 1 + _publicKey.PublicKey.Length); } }

        #endregion
    }

    public class DnssecPublicKey
    {
        #region variables

        protected byte[] _publicKey;

        #endregion

        #region constructor

        protected DnssecPublicKey()
        { }

        protected DnssecPublicKey(byte[] publicKey)
        {
            _publicKey = publicKey;
        }

        #endregion

        #region static

        public static DnssecPublicKey Parse(DnssecAlgorithm algorithm, byte[] publicKey)
        {
            switch (algorithm)
            {
                case DnssecAlgorithm.RSA_MD5:
                case DnssecAlgorithm.RSA_SHA1:
                case DnssecAlgorithm.RSA_SHA256:
                case DnssecAlgorithm.RSA_SHA512:
                    return new DnssecRsaPublicKey(publicKey);

                default:
                    return new DnssecPublicKey(publicKey);
            }
        }

        #endregion

        #region public

        public void WriteTo(Stream s)
        {
            s.Write(_publicKey);
        }

        public override bool Equals(object obj)
        {
            if (obj is null)
                return false;

            if (ReferenceEquals(this, obj))
                return true;

            if (obj is DnssecPublicKey other)
                return BinaryNumber.Equals(_publicKey, other._publicKey);

            return false;
        }

        public override int GetHashCode()
        {
            return _publicKey.GetHashCode();
        }

        public override string ToString()
        {
            return Convert.ToBase64String(_publicKey);
        }

        #endregion

        #region properties

        public byte[] PublicKey
        { get { return _publicKey; } }

        #endregion
    }

    public class DnssecRsaPublicKey : DnssecPublicKey
    {
        #region variables

        readonly RSAParameters _rsaPublicKey;

        #endregion

        #region constructor

        public DnssecRsaPublicKey(RSAParameters rsaPublicKey)
        {
            _rsaPublicKey = rsaPublicKey;

            if (_rsaPublicKey.Exponent.Length < 256)
            {
                _publicKey = new byte[1 + _rsaPublicKey.Exponent.Length + _rsaPublicKey.Modulus.Length];
                _publicKey[0] = (byte)_rsaPublicKey.Exponent.Length;
                Buffer.BlockCopy(_rsaPublicKey.Exponent, 0, _publicKey, 1, _rsaPublicKey.Exponent.Length);
                Buffer.BlockCopy(_rsaPublicKey.Modulus, 0, _publicKey, 1 + _rsaPublicKey.Exponent.Length, _rsaPublicKey.Modulus.Length);
            }
            else
            {
                byte[] bufferExponentLength = BitConverter.GetBytes(Convert.ToUInt16(_rsaPublicKey.Exponent.Length));
                Array.Reverse(bufferExponentLength);

                _publicKey = new byte[3 + _rsaPublicKey.Exponent.Length + _rsaPublicKey.Modulus.Length];
                Buffer.BlockCopy(bufferExponentLength, 0, _publicKey, 1, 2);
                Buffer.BlockCopy(_rsaPublicKey.Exponent, 0, _publicKey, 3, _rsaPublicKey.Exponent.Length);
                Buffer.BlockCopy(_rsaPublicKey.Modulus, 0, _publicKey, 3 + _rsaPublicKey.Exponent.Length, _rsaPublicKey.Modulus.Length);
            }
        }

        public DnssecRsaPublicKey(byte[] publicKey)
            : base(publicKey)
        {
            if (_publicKey[0] == 0)
            {
                byte[] bufferExponentLength = new byte[2];
                Buffer.BlockCopy(_publicKey, 1, bufferExponentLength, 0, 2);
                Array.Reverse(bufferExponentLength);

                int exponentLength = BitConverter.ToUInt16(bufferExponentLength, 0);
                int modulusLength = _publicKey.Length - exponentLength - 3;

                _rsaPublicKey.Exponent = new byte[exponentLength];
                _rsaPublicKey.Modulus = new byte[modulusLength];

                Buffer.BlockCopy(_publicKey, 3, _rsaPublicKey.Exponent, 0, exponentLength);
                Buffer.BlockCopy(_publicKey, 3 + exponentLength, _rsaPublicKey.Modulus, 0, modulusLength);
            }
            else
            {
                int exponentLength = _publicKey[0];
                int modulusLength = _publicKey.Length - exponentLength - 1;

                _rsaPublicKey.Exponent = new byte[exponentLength];
                _rsaPublicKey.Modulus = new byte[modulusLength];

                Buffer.BlockCopy(_publicKey, 1, _rsaPublicKey.Exponent, 0, exponentLength);
                Buffer.BlockCopy(_publicKey, 1 + exponentLength, _rsaPublicKey.Modulus, 0, modulusLength);
            }
        }

        #endregion

        #region public

        public override string ToString()
        {
            return Convert.ToBase64String(_rsaPublicKey.Exponent) + " " + Convert.ToBase64String(_rsaPublicKey.Modulus);
        }

        #endregion

        #region properties

        public RSAParameters RsaPublicKey
        { get { return _rsaPublicKey; } }

        #endregion
    }
}
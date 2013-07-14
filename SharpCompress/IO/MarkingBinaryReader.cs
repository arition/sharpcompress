﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SharpCompress.IO
{
    internal class MarkingBinaryReader : BinaryReader
    {
        private byte[] _salt;
        private string _password = "test";
        private byte[] _aesInitializationVector = new byte[16];
        private byte[] _aesKey = new byte[16];
        private Rijndael _rijndael;
        private Queue<byte> _data = new Queue<byte>();

        public MarkingBinaryReader(Stream stream)
            : base(stream)
        {
        }

        public long CurrentReadByteCount { get; private set; }

        internal byte[] Salt
        {
            get { return _salt; }
            set
            {
                _salt = value;
                if (value != null) InitializeAes();

            }
        }

        private void InitializeAes()
        {
            _rijndael = new RijndaelManaged() { Padding = PaddingMode.None };
            int rawLength = 2 * _password.Length;
            byte[] rawPassword = new byte[rawLength + 8];
            byte[] passwordBytes = Encoding.UTF8.GetBytes(_password);
            for (int i = 0; i < _password.Length; i++)
            {
                rawPassword[i * 2] = passwordBytes[i];
                rawPassword[i * 2 + 1] = 0;
            }
            for (int i = 0; i < _salt.Length; i++)
            {
                rawPassword[i + rawLength] = _salt[i];
            }

            var sha = new SHA1Managed();

            const int noOfRounds = (1 << 18);
            IList<byte> bytes = new List<byte>();
            byte[] digest;
            for (int i = 0; i < noOfRounds; i++)
            {
                bytes.AddRange(rawPassword);

                bytes.AddRange(new[] { (byte)i, (byte)(i >> 8), (byte)(i >> 16) });
                if (i % (noOfRounds / 16) == 0)
                {
                    digest = sha.ComputeHash(bytes.ToArray());
                    _aesInitializationVector[i / (noOfRounds / 16)] = digest[19];
                }
            }

            digest = sha.ComputeHash(bytes.ToArray());

            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    _aesKey[i * 4 + j] = (byte)
                        (((digest[i * 4] * 0x1000000) & 0xff000000 |
                        ((digest[i * 4 + 1] * 0x10000) & 0xff0000) |
                          ((digest[i * 4 + 2] * 0x100) & 0xff00) |
                          digest[i * 4 + 3] & 0xff) >> (j * 8));

            _rijndael.IV = new byte[16];
            _rijndael.Key = _aesKey;
            _rijndael.BlockSize = 16 * 8;
        }


        public void Mark()
        {
            CurrentReadByteCount = 0;
        }

        public override int Read()
        {
            CurrentReadByteCount += 4;
            return base.Read();
        }

        public override int Read(byte[] buffer, int index, int count)
        {
            CurrentReadByteCount += count;
            return base.Read(buffer, index, count);
        }

        public override int Read(char[] buffer, int index, int count)
        {
            throw new NotImplementedException();
        }

        public override bool ReadBoolean()
        {
            CurrentReadByteCount++;
            return base.ReadBoolean();
        }

        public override byte ReadByte()
        {
            return ReadBytes(1).Single();
        }

        public override byte[] ReadBytes(int count)
        {
            CurrentReadByteCount += count;
            return UseEncryption ?
                ReadAndDecryptBytes(count)
                : base.ReadBytes(count);
        }

        protected bool UseEncryption
        {
            get { return Salt != null; }
        }

        private byte[] ReadAndDecryptBytes(int count)
        {
            int queueSize = _data.Count;
            int sizeToRead = count - queueSize;

            if (sizeToRead > 0)
            {
                int alignedSize = sizeToRead + ((~sizeToRead + 1) & 0xf);
                for (int i = 0; i < alignedSize / 16; i++)
                {
                    //long ax = System.currentTimeMillis();
                    byte[] cipherText = base.ReadBytes(16);

                    byte[] plainText = new byte[16];
                    var decryptor = _rijndael.CreateDecryptor();
                    using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                    {
                        using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                        {

                            csDecrypt.ReadFully(plainText);

                        }
                    }


                    for (int j = 0; j < plainText.Length; j++)
                    {
                        _data.Enqueue((byte)(plainText[j] ^ _aesInitializationVector[j % 16])); //32:114, 33:101

                    }

                    for (int j = 0; j < _aesInitializationVector.Length; j++)
                    {
                        _aesInitializationVector[j] = cipherText[j];
                    }

                }

            }

            var decryptedBytes = new byte[count];

            for (int i = 0; i < count; i++)
            {

                decryptedBytes[i] = _data.Dequeue();
                Console.Write(decryptedBytes[i].ToString("x2") + " ");
            }
            Console.WriteLine("");
            return decryptedBytes;
        }

        public override char ReadChar()
        {
            throw new NotImplementedException();
        }

        public override char[] ReadChars(int count)
        {
            throw new NotImplementedException();
        }

#if !PORTABLE
        public override decimal ReadDecimal()
        {
            return ByteArrayToDecimal(ReadBytes(16), 0);
        }

        private decimal ByteArrayToDecimal(byte[] src, int offset)
        {
            //http://stackoverflow.com/a/16984356/385387
            var i1 = BitConverter.ToInt32(src, offset);
            var i2 = BitConverter.ToInt32(src, offset + 4);
            var i3 = BitConverter.ToInt32(src, offset + 8);
            var i4 = BitConverter.ToInt32(src, offset + 12);

            return new decimal(new[] { i1, i2, i3, i4 });
        }
#endif

        public override double ReadDouble()
        {
            return BitConverter.ToDouble(ReadBytes(8), 0);
        }

        public override short ReadInt16()
        {
            return BitConverter.ToInt16(ReadBytes(2), 0);
        }

        public override int ReadInt32()
        {
            return BitConverter.ToInt32(ReadBytes(4), 0);
        }

        public override long ReadInt64()
        {
            return BitConverter.ToInt64(ReadBytes(8), 0);
        }

        public override sbyte ReadSByte()
        {
            return (sbyte)ReadByte();
        }

        public override float ReadSingle()
        {
            return BitConverter.ToSingle(ReadBytes(4), 0);
        }

        public override string ReadString()
        {
            throw new NotImplementedException();
        }

        public override ushort ReadUInt16()
        {
            return BitConverter.ToUInt16(ReadBytes(2), 0);
        }

        public override uint ReadUInt32()
        {
            return BitConverter.ToUInt32(ReadBytes(4), 0);
        }

        public override ulong ReadUInt64()
        {
            return BitConverter.ToUInt64(ReadBytes(8), 0);
        }

        public void ClearQueue()
        {
            _data.Clear();
        }

        public void SkipQueue()
        {
            var position = BaseStream.Position;
            BaseStream.Position = position + _data.Count;
            ClearQueue();
        }
    }
}
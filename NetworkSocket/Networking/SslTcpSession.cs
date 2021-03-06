﻿using NetworkSocket.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkSocket
{
    /// <summary>
    /// 表示SSL的Tcp会话对象  
    /// </summary>        
    internal sealed class SslTcpSession : TcpSessionBase
    {
        /// <summary>
        /// 目标主机
        /// </summary>
        private string targetHost;

        /// <summary>
        /// SSL数据流
        /// </summary>
        private SslStream sslStream;

        /// <summary>
        /// 服务器证书
        /// 不为null时表示服务器会话对象
        /// </summary>
        private X509Certificate certificate;

        /// <summary>
        /// 远程证书验证回调
        /// </summary>
        private RemoteCertificateValidationCallback certificateValidationCallback;

        /// <summary>
        /// 缓冲区范围
        /// </summary>
        private readonly ArraySegment<byte> bufferRange = BufferPool.AllocBuffer();


        /// <summary>
        /// 获取会话是否提供SSL/TLS安全
        /// </summary>
        public override bool IsSecurity
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// 表示SSL服务器会话对象
        /// </summary>  
        /// <param name="certificate">服务器证书</param>
        ///  <exception cref="ArgumentNullException"></exception>
        public SslTcpSession(X509Certificate certificate)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException();
            }
            this.certificate = certificate;
            this.certificateValidationCallback = (a, b, c, d) => true;
        }

        /// <summary>
        /// 表示SSL客户端会话对象
        /// </summary>  
        /// <param name="targetHost">目标主机</param>
        /// <param name="certificateValidationCallback">远程证书验证回调</param>
        /// <exception cref="ArgumentNullException"></exception>
        public SslTcpSession(string targetHost, RemoteCertificateValidationCallback certificateValidationCallback)
        {
            if (string.IsNullOrEmpty(targetHost) == true)
            {
                throw new ArgumentNullException("targetHost");
            }
            this.targetHost = targetHost;
            this.certificateValidationCallback = certificateValidationCallback;
        }

        /// <summary>
        /// 绑定一个Socket对象
        /// </summary>
        /// <param name="socket">套接字</param>
        public override void Bind(Socket socket)
        {
            var nsStream = new NetworkStream(socket, false);
            this.sslStream = new SslStream(nsStream, false, this.certificateValidationCallback);
            base.Bind(socket);
        }

        /// <summary>
        /// 开始循环接收数据
        /// </summary>
        /// <exception cref="AuthenticationException"></exception>
        public override async void LoopReceive()
        {
            if (await this.AuthenticateAsync() == true)
            {
                this.ReceiveAsync();
            }
        }

        /// <summary>
        /// SSL验证
        /// </summary>
        /// <returns></returns>
        private async Task<bool> AuthenticateAsync()
        {
            // SSL客户端
            if (this.certificate == null)
            {
                this.sslStream.AuthenticateAsClient(this.targetHost);
                return true;
            }

            try
            {
                await this.sslStream.AuthenticateAsServerAsync(this.certificate);
                return true;
            }
            catch (Exception)
            {
                ((ISession)this).Close();
                return false;
            }
        }


        /// <summary>
        /// 尝试开始接收数据
        /// </summary>
        private async void ReceiveAsync()
        {
            if (this.IsConnected == false)
            {
                return;
            }

            var read = await this.ReadAsync().ConfigureAwait(false);
            if (read <= 0)
            {
                this.DisconnectHandler(this);
                return;
            }

            lock (this.StreamReader.SyncRoot)
            {
                this.StreamReader.Stream.Seek(0, SeekOrigin.End);
                this.StreamReader.Stream.Write(this.bufferRange.Array, this.bufferRange.Offset, read);
                this.StreamReader.Stream.Seek(0, SeekOrigin.Begin);
            }

            await this.ReceiveAsyncHandler(this);
            this.ReceiveAsync();
        }

        /// <summary>
        /// 返回读取到的数据长度   
        /// 异常则返回0
        /// </summary>
        /// <returns></returns>
        private async Task<int> ReadAsync()
        {
            try
            {
                return await this.sslStream.ReadAsync(this.bufferRange.Array, this.bufferRange.Offset, this.bufferRange.Count);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// 同步发送数据
        /// </summary>
        /// <param name="byteRange">数据范围</param>  
        /// <exception cref="ArgumentNullException"></exception>        
        /// <exception cref="SocketException"></exception>
        /// <returns></returns>
        public override int Send(ArraySegment<byte> byteRange)
        {
            if (byteRange == null)
            {
                throw new ArgumentNullException();
            }

            if (this.IsConnected == false)
            {
                throw new SocketException((int)SocketError.NotConnected);
            }

            this.sslStream.Write(byteRange.Array, byteRange.Offset, byteRange.Count);
            return byteRange.Count;
        }

        /// <summary>
        /// 同步发送数据
        /// </summary>
        /// <param name="buffer">数据</param>
        /// <returns></returns>
        public override int Send(byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException();
            }

            if (this.IsConnected == false)
            {
                throw new SocketException((int)SocketError.NotConnected);
            }

            this.sslStream.Write(buffer);
            return buffer.Length;
        }


        /// <summary>
        /// 释放资源
        /// </summary>
        /// <param name="disposing">是否也释放托管资源</param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing == true)
            {
                this.targetHost = null;
                this.sslStream = null;
                this.certificate = null;
                this.certificateValidationCallback = null;
            }
        }
    }
}


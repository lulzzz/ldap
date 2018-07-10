using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Telefrek.Security.LDAP.Protocol;

namespace Telefrek.Security.LDAP.IO
{
    /// <summary>
    /// Class for handling the protocol level communications
    /// </summary>
    internal class LDAPConnection : ILDAPConnection
    {
        int _globalMessgeId = 0;
        TcpClient _conn;
        SslStream _transport;
        NetworkStream _raw;
        MessagePump _pump;
        int _messageId = 0;
        bool _sslEnabled;

        /// <summary>
        /// Internal constructor used to establish streams
        /// </summary>
        /// <param name="sslEnabled"></param>
        internal LDAPConnection(bool sslEnabled)
        {
            _conn = new TcpClient();
            _sslEnabled = sslEnabled;
            _transport = null;
        }

        /// <summary>
        /// Connect to the given host on the port asynchronously
        /// </summary>
        /// <param name="host">The host to connect to</param>
        /// <param name="port">The port to use for communication</param>
        public async Task ConnectAsync(string host, int port)
        {
            try
            {
                await _conn.ConnectAsync(host, port);
                if (_sslEnabled)
                {
                    _raw = _conn.GetStream();

                    var options = new SslClientAuthenticationOptions
                    {
                        TargetHost = host,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls,
                        ClientCertificates = null,
                        LocalCertificateSelectionCallback = null,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                        RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                        {
                            // Accept all...bad idea
                            return true;
                        },
                        ApplicationProtocols = new List<SslApplicationProtocol>() { SslApplicationProtocol.Http11 },
                        EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                    };

                    _transport = new SslStream(_raw);
                    await (_transport as SslStream).AuthenticateAsClientAsync(options, CancellationToken.None);
                    Reader = new LDAPReader(_transport);
                    Writer = new LDAPWriter(_transport);
                }
                else
                {
                    _raw = _conn.GetStream();
                    _transport = null;
                    Reader = new LDAPReader(_raw);
                    Writer = new LDAPWriter(_raw);
                }

                // Create the pump and start it
                _pump = new MessagePump(Reader, _raw);
                _pump.Start();
            }
            catch (Exception e)
            {
                throw new LDAPException("Failed to connect", e);
            }
        }

        public async Task CloseAsync()
        {
            await TryQueueOperation(new UnbindRequest(), CancellationToken.None);
            await _pump.StopAsync();
        }

        public async Task<ICollection<ProtocolOperation>> TryQueueOperation(ProtocolOperation op, CancellationToken token)
        {
            op.MessageId = Interlocked.Increment(ref _globalMessgeId);

            var msgs = new List<ProtocolOperation>();

            try
            {
                var response = _pump.GetResponse(op.MessageId, token);
                await op.WriteAsync(Writer);

                if (op.HasResponse)
                {
                    op = null;
                    while ((op = await response) != null)
                    {
                        msgs.Add(op);
                        if (op.IsTerminating)
                            break;
                    }
                }
            }
            catch (AggregateException)
            {
            }

            return msgs;
        }

        /// <summary>
        /// Gets the connection reader
        /// </summary>
        public LDAPReader Reader { get; private set; }

        /// <summary>
        /// Gets the connection writer
        /// </summary>
        public LDAPWriter Writer { get; private set; }

        /// <summary>
        /// Dispose of all connection resources
        /// </summary>
        public void Dispose() => Dispose(true);

        bool _isDisposed = false;

        void Dispose(bool disposing)
        {
            if (disposing && !_isDisposed)
            {
                if (_transport != null)
                {
                    _transport.Flush();
                    _transport.Close();
                }
                else if (_raw != null)
                {
                    _raw.Flush();
                    _raw.Close();
                }

                if (_conn != null)
                    _conn.Close();

                _transport = null;
                _raw = null;
                _conn = null;

                // Notify GC to ignore
                GC.SuppressFinalize(this);
            }

            _isDisposed = true;
        }

        ~LDAPConnection() => Dispose(false);
    }
}
using System;
using System.Threading.Tasks;

namespace Telefrek.Security.LDAP.Protocol
{
    internal class BindResponse : LDAPResponse
    {
        public override ProtocolOp Operation => ProtocolOp.BIND_RESPONSE;

        // Nothing more to do here
        protected override Task ReadResponseAsync(LDAPReader reader) => Task.CompletedTask;
    }
}
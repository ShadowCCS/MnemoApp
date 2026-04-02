using System;
using System.Threading;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Tools;

public sealed class ToolDispatchAmbient : IToolDispatchAmbient
{
    private readonly AsyncLocal<string?> routingKey = new();

    public string? ConversationRoutingKey => routingKey.Value;

    public IDisposable Enter(string? conversationRoutingKey)
    {
        var prev = routingKey.Value;
        routingKey.Value = conversationRoutingKey;
        return new Scope(this, prev);
    }

    private sealed class Scope : IDisposable
    {
        private readonly ToolDispatchAmbient owner;
        private readonly string? previous;

        public Scope(ToolDispatchAmbient owner, string? previous)
        {
            this.owner = owner;
            this.previous = previous;
        }

        public void Dispose() => owner.routingKey.Value = previous;
    }
}

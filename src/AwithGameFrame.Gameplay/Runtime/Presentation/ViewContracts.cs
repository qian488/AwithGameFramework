using System;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Resources;

namespace AwithGameFrame.Extensions.Presentation
{
    public sealed class ViewRequest
    {
        public ViewRequest(string key, int layer = 0, bool modal = false, bool takesFocus = true)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("View key cannot be empty.", nameof(key));
            Key = key; Layer = layer; Modal = modal; TakesFocus = takesFocus;
        }
        public string Key { get; }
        public int Layer { get; }
        public bool Modal { get; }
        public bool TakesFocus { get; }
    }

    public readonly struct ViewInput
    {
        public ViewInput(string action, object payload = null)
        {
            if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Input action cannot be empty.", nameof(action));
            Action = action; Payload = payload;
        }
        public string Action { get; }
        public object Payload { get; }
    }

    public readonly struct ViewState
    {
        public ViewState(int order, bool covered, bool inputEnabled, bool focused)
        { Order = order; IsCovered = covered; IsInputEnabled = inputEnabled; IsFocused = focused; }
        public int Order { get; }
        public bool IsCovered { get; }
        public bool IsInputEnabled { get; }
        public bool IsFocused { get; }
    }

    public interface IView : IAsyncDisposable
    {
        Task ShowAsync(CancellationToken cancellationToken);
        Task HideAsync(CancellationToken cancellationToken);
        void ApplyState(ViewState state);
        bool HandleInput(ViewInput input);
    }

    public interface IViewFactory
    {
        /// <summary>Return every created view, including late success after cancellation, so the service can release it. The resource scope is borrowed; only the service disposes it.</summary>
        Task<IView> CreateAsync(ViewRequest request, ResourceScope resources, CancellationToken cancellationToken);
    }

    public sealed class ViewHandle : IAsyncDisposable
    {
        private readonly ViewService owner;
        internal ViewHandle(ViewService owner, long id, ViewRequest request, IView view, ResourceScope resources)
        { this.owner = owner; InstanceId = id; Request = request; View = view; Resources = resources; }
        public long InstanceId { get; }
        public ViewRequest Request { get; }
        internal IView View { get; }
        internal ResourceScope Resources { get; }
        public bool IsOpen => owner.Contains(this);
        public bool IsFocused => ReferenceEquals(owner.Focused, this);
        public Task CloseAsync() => owner.CloseAsync(this);
        public ValueTask DisposeAsync() => new ValueTask(CloseAsync());
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Core.Events;
using AwithGameFrame.Core.Hosting;

namespace AwithGameFrame.Gameplay.Input
{
    public enum InputActionPhase { Pressed, Held, Released }
    public enum BindingConflictPolicy { Reject, ReplaceExisting }

    public sealed class InputActionEvent
    {
        internal InputActionEvent(string contextId, string action, InputActionPhase phase, float value)
        { ContextId = contextId; Action = action; Phase = phase; Value = value; }
        public string ContextId { get; }
        public string Action { get; }
        public InputActionPhase Phase { get; }
        public float Value { get; }
    }

    public interface IInputBackend
    {
        bool IsFocused { get; }
        float ReadControl(string control);
    }

    public sealed class InputUpdateResult
    {
        internal InputUpdateResult(List<InputActionEvent> events, List<Exception> errors)
        { Events = events.AsReadOnly(); Errors = errors.AsReadOnly(); }
        public IReadOnlyList<InputActionEvent> Events { get; }
        public IReadOnlyList<Exception> Errors { get; }
        public void ThrowIfFailed()
        { if (Errors.Count != 0) throw new AggregateException("Input listeners reported errors.", Errors); }
    }

    /// <summary>Instance input routing on its creating thread. Mutations cannot reenter input publication.</summary>
    public sealed class InputService : IDisposable
    {
        private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
        private readonly EventBus _events;
        private readonly List<InputContext> _contexts = new List<InputContext>();
        private readonly HashSet<(string Context, string Action)> _delivered = new HashSet<(string, string)>();
        private readonly HashSet<(string Context, string Action)> _suppressed = new HashSet<(string, string)>();
        private IInputRouter _router;
        private bool _enabled = true, _focused = true, _disposed, _busy;
        private long _sequence;

        public InputService(EventBus events, IInputRouter router = null)
        { _events = events ?? throw new ArgumentNullException(nameof(events)); _router = router; }
        public bool IsEnabled { get { VerifyThread(); return _enabled; } }
        public bool IsFocused { get { VerifyThread(); return _focused; } }

        public void SetRouter(IInputRouter router) { VerifyMutation(); _router = router; }

        public IReadOnlyList<InputBindingSnapshot> CaptureBindings()
        {
            VerifyThread();
            if (_disposed) throw new ObjectDisposedException(nameof(InputService));
            var result = new List<InputBindingSnapshot>();
            foreach (var context in _contexts)
                foreach (var pair in context.Bindings)
                    result.Add(new InputBindingSnapshot(context.Id, pair.Key, pair.Value.Control, pair.Value.Scale, pair.Value.DeadZone));
            return result.AsReadOnly();
        }

        /// <summary>Validates an exact complete snapshot before committing. Publication errors occur after commitment.</summary>
        public void ApplyBindings(IReadOnlyList<InputBindingSnapshot> bindings)
        {
            VerifyMutation();
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            var replacements = new Dictionary<InputContext, Dictionary<string, InputContext.Binding>>();
            var controls = new Dictionary<InputContext, HashSet<string>>();
            foreach (var context in _contexts)
            {
                replacements.Add(context, new Dictionary<string, InputContext.Binding>(StringComparer.Ordinal));
                controls.Add(context, new HashSet<string>(StringComparer.Ordinal));
            }
            foreach (var snapshot in bindings)
            {
                if (snapshot == null) throw new ArgumentException("A binding snapshot cannot contain null.", nameof(bindings));
                ValidateName(snapshot.ContextId, nameof(bindings));
                ValidateBinding(snapshot.Action, snapshot.Control, snapshot.Scale, snapshot.DeadZone);
                var context = _contexts.Find(item => item.Id == snapshot.ContextId);
                if (context == null || !context.Bindings.ContainsKey(snapshot.Action)) throw new ArgumentException("Snapshot contains an unknown context or action.", nameof(bindings));
                if (replacements[context].ContainsKey(snapshot.Action) || !controls[context].Add(snapshot.Control))
                    throw new ArgumentException("Snapshot contains a repeated action or conflicting control.", nameof(bindings));
                replacements[context].Add(snapshot.Action, new InputContext.Binding { Control = snapshot.Control, Scale = snapshot.Scale, DeadZone = snapshot.DeadZone });
            }
            foreach (var context in _contexts)
                if (replacements[context].Count != context.Bindings.Count) throw new ArgumentException("Snapshot must cover every existing action exactly once.", nameof(bindings));
            var pending = CollectReleases(_contexts);
            foreach (var context in _contexts)
            {
                context.Bindings.Clear();
                foreach (var pair in replacements[context]) context.Bindings.Add(pair.Key, pair.Value);
            }
            Publish(pending).ThrowIfFailed();
        }

        public InputContext CreateContext(string id, int priority = 0, bool modal = false)
        {
            VerifyMutation();
            ValidateName(id, nameof(id));
            foreach (var existing in _contexts)
                if (existing.Id == id) throw new ArgumentException("Input context ID already exists.", nameof(id));
            var context = new InputContext(this, id, priority, modal, ++_sequence);
            _contexts.Add(context);
            _contexts.Sort((left, right) =>
            {
                int priorityOrder = right.Priority.CompareTo(left.Priority);
                return priorityOrder != 0 ? priorityOrder : right.Sequence.CompareTo(left.Sequence);
            });
            return context;
        }

        public InputUpdateResult SetEnabled(bool enabled)
        {
            VerifyMutation();
            _enabled = enabled;
            return enabled ? EmptyResult() : ReleaseAll();
        }

        public InputUpdateResult SetFocused(bool focused)
        {
            VerifyMutation();
            _focused = focused;
            return focused ? EmptyResult() : ReleaseAll();
        }

        public InputUpdateResult Poll(IInputBackend backend)
        {
            VerifyMutation();
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            if (!backend.IsFocused) return SetFocused(false);
            _focused = true;
            if (!_enabled) return EmptyResult();
            var values = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var context in _contexts)
                if (context.Enabled)
                    foreach (var binding in context.Bindings.Values)
                        if (!values.ContainsKey(binding.Control)) values.Add(binding.Control, backend.ReadControl(binding.Control));
            return ProcessFrame(values);
        }

        public InputUpdateResult ProcessFrame(IReadOnlyDictionary<string, float> controls)
        {
            VerifyMutation();
            if (controls == null) throw new ArgumentNullException(nameof(controls));
            // Validate a whole frame before changing any action state.
            foreach (var value in controls.Values)
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("Control values must be finite.", nameof(controls));
            var pending = new List<InputActionEvent>();
            var consumed = new HashSet<string>(StringComparer.Ordinal);
            bool blocked = !_enabled || !_focused;
            foreach (var context in _contexts)
            {
                foreach (var pair in context.Bindings)
                {
                    var binding = pair.Value;
                    float next = 0;
                    if (!blocked && context.Enabled && !consumed.Contains(binding.Control) && controls.TryGetValue(binding.Control, out var raw))
                    {
                        raw = Math.Max(-1, Math.Min(1, raw));
                        if (Math.Abs(raw) > binding.DeadZone) next = raw * binding.Scale;
                        if (next != 0) consumed.Add(binding.Control);
                    }
                    AppendTransition(pending, context.Id, pair.Key, binding, next);
                }
                if (context.Enabled && context.Modal) blocked = true;
            }
            return Publish(pending);
        }

        public void Dispose()
        {
            VerifyThread();
            if (_disposed) return;
            VerifyMutation();
            var pending = CollectReleases(_contexts);
            foreach (var context in _contexts) context.Detach();
            _contexts.Clear();
            _disposed = true;
            Publish(pending).ThrowIfFailed();
        }

        internal void Bind(InputContext context, string action, string control, float scale, float deadZone, BindingConflictPolicy conflictPolicy)
        {
            VerifyMutation();
            ValidateBinding(action, control, scale, deadZone);
            if (conflictPolicy != BindingConflictPolicy.Reject && conflictPolicy != BindingConflictPolicy.ReplaceExisting)
                throw new ArgumentOutOfRangeException(nameof(conflictPolicy));
            string conflicting = null;
            foreach (var pair in context.Bindings)
                if (pair.Key != action && pair.Value.Control == control) { conflicting = pair.Key; break; }
            if (conflicting != null && conflictPolicy == BindingConflictPolicy.Reject)
                throw new InvalidOperationException("Control is already bound to action '" + conflicting + "' in this context.");
            var pending = new List<InputActionEvent>();
            if (conflicting != null) RemoveBinding(context, conflicting, pending);
            RemoveBinding(context, action, pending);
            context.Bindings.Add(action, new InputContext.Binding { Control = control, Scale = scale, DeadZone = deadZone });
            Publish(pending).ThrowIfFailed();
        }

        internal void SetContextEnabled(InputContext context, bool enabled)
        {
            VerifyMutation();
            context.Enabled = enabled;
            if (!enabled) Publish(CollectReleases(new[] { context })).ThrowIfFailed();
        }

        internal void RemoveContext(InputContext context)
        {
            VerifyMutation();
            var pending = CollectReleases(new[] { context });
            _contexts.Remove(context);
            context.Detach();
            Publish(pending).ThrowIfFailed();
        }

        private static void RemoveBinding(InputContext context, string action, List<InputActionEvent> pending)
        {
            if (!context.Bindings.TryGetValue(action, out var binding)) return;
            AppendTransition(pending, context.Id, action, binding, 0);
            context.Bindings.Remove(action);
        }

        private InputUpdateResult ReleaseAll() => Publish(CollectReleases(_contexts));

        private static List<InputActionEvent> CollectReleases(IEnumerable<InputContext> contexts)
        {
            var pending = new List<InputActionEvent>();
            foreach (var context in contexts)
                foreach (var pair in context.Bindings) AppendTransition(pending, context.Id, pair.Key, pair.Value, 0);
            return pending;
        }

        private static void AppendTransition(List<InputActionEvent> pending, string context, string action, InputContext.Binding binding, float next)
        {
            if (next != 0) pending.Add(new InputActionEvent(context, action,
                binding.Value == 0 ? InputActionPhase.Pressed : InputActionPhase.Held, next));
            else if (binding.Value != 0) pending.Add(new InputActionEvent(context, action, InputActionPhase.Released, 0));
            binding.Value = next;
        }

        private InputUpdateResult Publish(List<InputActionEvent> pending)
        {
            var errors = new List<Exception>();
            var delivered = new List<InputActionEvent>();
            _busy = true;
            try
            {
                foreach (var message in pending)
                {
                    var key = (message.ContextId, message.Action);
                    bool consumed = false;
                    try { consumed = _router?.Route(message) ?? false; }
                    catch (Exception error) { errors.Add(error); consumed = true; }
                    if (message.Phase == InputActionPhase.Released)
                    {
                        _suppressed.Remove(key);
                        if (_delivered.Remove(key)) Deliver(message, delivered, errors);
                    }
                    else if (consumed)
                    {
                        _suppressed.Add(key);
                        // Gameplay must receive its release even when a modal consumes the physical action.
                        if (_delivered.Remove(key)) Deliver(new InputActionEvent(message.ContextId, message.Action, InputActionPhase.Released, 0), delivered, errors);
                    }
                    else if (!_suppressed.Contains(key))
                    {
                        _delivered.Add(key);
                        Deliver(message, delivered, errors);
                    }
                }
            }
            finally { _busy = false; }
            return new InputUpdateResult(delivered, errors);
        }

        private void Deliver(InputActionEvent message, List<InputActionEvent> delivered, List<Exception> errors)
        {
            delivered.Add(message);
            try { errors.AddRange(_events.Publish(message).Errors); }
            catch (Exception error) { errors.Add(error); }
        }

        private static void ValidateBinding(string action, string control, float scale, float deadZone)
        {
            ValidateName(action, nameof(action));
            ValidateName(control, nameof(control));
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale == 0 || Math.Abs(scale) > 1) throw new ArgumentOutOfRangeException(nameof(scale));
            if (float.IsNaN(deadZone) || float.IsInfinity(deadZone) || deadZone < 0 || deadZone >= 1) throw new ArgumentOutOfRangeException(nameof(deadZone));
        }

        private static InputUpdateResult EmptyResult() => new InputUpdateResult(new List<InputActionEvent>(), new List<Exception>());
        private static void ValidateName(string value, string parameter)
        { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Name cannot be empty.", parameter); }
        private void VerifyThread()
        { if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Input must be used on its creating thread."); }
        private void VerifyMutation()
        {
            VerifyThread();
            if (_busy) throw new InvalidOperationException("Input listeners cannot reenter input mutations; dispatch the change for a later frame.");
            if (_disposed) throw new ObjectDisposedException(nameof(InputService));
        }
    }

    public sealed class InputContext : IDisposable
    {
        private InputService _service;
        internal InputContext(InputService service, string id, int priority, bool modal, long sequence)
        { _service = service; Id = id; Priority = priority; Modal = modal; Sequence = sequence; }
        public string Id { get; }
        public int Priority { get; }
        public bool Modal { get; }
        internal long Sequence { get; }
        internal bool Enabled { get; set; } = true;
        internal Dictionary<string, Binding> Bindings { get; } = new Dictionary<string, Binding>(StringComparer.Ordinal);
        public void Bind(string action, string control, float scale = 1, float deadZone = 0.1f,
            BindingConflictPolicy conflictPolicy = BindingConflictPolicy.Reject)
        { Service.Bind(this, action, control, scale, deadZone, conflictPolicy); }
        public void Rebind(string action, string control, BindingConflictPolicy conflictPolicy = BindingConflictPolicy.Reject,
            float scale = 1, float deadZone = 0.1f)
        { Service.Bind(this, action, control, scale, deadZone, conflictPolicy); }
        public void SetEnabled(bool enabled) { Service.SetContextEnabled(this, enabled); }
        public void Dispose() { _service?.RemoveContext(this); }
        private InputService Service => _service ?? throw new ObjectDisposedException(nameof(InputContext));
        internal void Detach() { _service = null; Bindings.Clear(); }
        internal sealed class Binding { internal string Control; internal float Scale, DeadZone, Value; }
    }

    public sealed class InputModule : GameModule
    {
        private readonly InputService _input;
        private readonly IInputBackend _backend;
        private bool _resumeEnabled;
        public InputModule(InputService input, IInputBackend backend)
        { _input = input ?? throw new ArgumentNullException(nameof(input)); _backend = backend ?? throw new ArgumentNullException(nameof(backend)); }
        public override string Id => "input";
        public override Task StartAsync(GameContext context, CancellationToken cancellationToken)
        { context.Services.Register(_input); return Task.CompletedTask; }
        public override void Tick(GameContext context, double deltaSeconds) { _input.Poll(_backend).ThrowIfFailed(); }
        public override Task PauseAsync(GameContext context, CancellationToken cancellationToken)
        { _resumeEnabled = _input.IsEnabled; _input.SetEnabled(false).ThrowIfFailed(); return Task.CompletedTask; }
        public override Task ResumeAsync(GameContext context, CancellationToken cancellationToken)
        { _input.SetEnabled(_resumeEnabled).ThrowIfFailed(); return Task.CompletedTask; }
        public override Task StopAsync(GameContext context, CancellationToken cancellationToken)
        { _input.Dispose(); return Task.CompletedTask; }
    }
}

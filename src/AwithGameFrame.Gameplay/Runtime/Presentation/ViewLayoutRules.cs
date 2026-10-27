using System;
using System.Collections.Generic;

namespace AwithGameFrame.Extensions.Presentation
{
    /// <summary>Immutable view facts used to calculate a layout without creating or changing views.</summary>
    public sealed class ViewLayoutItem
    {
        public ViewLayoutItem(long instanceId, ViewRequest request)
        {
            InstanceId = instanceId;
            Request = request ?? throw new ArgumentNullException(nameof(request));
        }

        public long InstanceId { get; }
        public ViewRequest Request { get; }
    }

    /// <summary>An independently calculated entry, ordered from the lowest view to the highest.</summary>
    public sealed class ViewLayoutEntry
    {
        internal ViewLayoutEntry(ViewLayoutItem item, ViewState state)
        { InstanceId = item.InstanceId; Request = item.Request; State = state; }

        public long InstanceId { get; }
        public ViewRequest Request { get; }
        public ViewState State { get; }
    }

    public static class ViewLayoutRules
    {
        /// <summary>Orders by layer and instance ID; the highest modal covers lower views and bounds focus selection. Does not mutate the supplied snapshot.</summary>
        public static IReadOnlyList<ViewLayoutEntry> Calculate(IReadOnlyList<ViewLayoutItem> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var ordered = new List<ViewLayoutItem>(source.Count);
            var identities = new HashSet<long>();
            foreach (var item in source)
            {
                if (item == null) throw new ArgumentException("A view layout cannot contain null.", nameof(source));
                if (!identities.Add(item.InstanceId)) throw new ArgumentException("View instance IDs must be unique.", nameof(source));
                ordered.Add(item);
            }
            ordered.Sort((a, b) => a.Request.Layer != b.Request.Layer
                ? a.Request.Layer.CompareTo(b.Request.Layer) : a.InstanceId.CompareTo(b.InstanceId));
            int modal = -1, focus = -1;
            for (int i = ordered.Count - 1; i >= 0; i--)
                if (ordered[i].Request.Modal) { modal = i; break; }
            for (int i = ordered.Count - 1; i >= Math.Max(0, modal); i--)
                if (ordered[i].Request.TakesFocus) { focus = i; break; }
            var result = new List<ViewLayoutEntry>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
                result.Add(new ViewLayoutEntry(ordered[i], new ViewState(i, i < modal, i >= modal, i == focus)));
            return result.AsReadOnly();
        }
    }
}

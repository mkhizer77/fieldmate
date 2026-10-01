using System.Collections.Generic;
using UnityEngine;

namespace Fieldmate.XR;

/// <summary>
/// Hides a hierarchy without deactivating it (#71): renderers, canvases and colliders are switched off and later put back
/// as they were, so components keep running (the machine's services, controls and router stay wired) while it is
/// invisible and can't be hit by a pointer.
/// </summary>
public sealed class HiddenObjects
{
    private readonly List<(Behaviour behaviour, bool enabled)> behaviours = new();
    private readonly List<(Renderer renderer, bool enabled)> renderers = new();
    private readonly List<(Collider collider, bool enabled)> colliders = new();

    public bool IsHidden { get; private set; }

    public void Hide(Transform root)
    {
        if (IsHidden || root == null)
        {
            return;
        }

        IsHidden = true;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            renderers.Add((r, r.enabled));
            r.enabled = false;
        }

        foreach (var c in root.GetComponentsInChildren<Canvas>(true))
        {
            behaviours.Add((c, c.enabled));
            c.enabled = false;
        }

        foreach (var c in root.GetComponentsInChildren<Collider>(true))
        {
            colliders.Add((c, c.enabled));
            c.enabled = false;
        }
    }

    public void Show()
    {
        if (!IsHidden)
        {
            return;
        }

        IsHidden = false;
        foreach (var (r, enabled) in renderers)
        {
            if (r != null) r.enabled = enabled;
        }

        foreach (var (b, enabled) in behaviours)
        {
            if (b != null) b.enabled = enabled;
        }

        foreach (var (c, enabled) in colliders)
        {
            if (c != null) c.enabled = enabled;
        }

        renderers.Clear();
        behaviours.Clear();
        colliders.Clear();
    }
}

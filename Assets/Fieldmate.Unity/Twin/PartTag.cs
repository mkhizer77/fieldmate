using UnityEngine;

namespace Fieldmate.Twin;

/// <summary>
/// Marks a scene object as a machine part from manual.json. Gaze, highlighting, tool calls and the manual validator
/// find parts by this id.
/// </summary>
[DisallowMultipleComponent]
public sealed class PartTag : MonoBehaviour
{
    [SerializeField] private string partId;

    public string PartId => partId;

    internal void SetPartId(string id) => partId = id;
}

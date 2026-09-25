using System;
using UnityEngine.XR.ARSubsystems;

namespace Fieldmate.XR;

/// <summary>
/// Converts saved-anchor ids to strings for local storage. Meta's anchor provider cannot list saved anchors, so the app
/// must remember their ids itself.
/// </summary>
public static class AnchorIdCodec
{
    public static string Encode(SerializableGuid id) => id.guid.ToString("N");

    public static bool TryDecode(string text, out SerializableGuid id)
    {
        if (!string.IsNullOrWhiteSpace(text) && Guid.TryParseExact(text.Trim(), "N", out var guid) && guid != Guid.Empty)
        {
            id = new SerializableGuid(guid);
            return true;
        }

        id = SerializableGuid.empty;
        return false;
    }
}

using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Fieldmate.Twin;

/// <summary>Machine definitions by tag id, from Addressables (#27). Unknown tags come back as null, not an error.</summary>
public static class MachineCatalog
{
    public static async Task<MachineDefinition> LoadAsync(int tagId)
    {
        var address = MachineDefinition.AddressFor(tagId);
        var locations = Addressables.LoadResourceLocationsAsync(address, typeof(MachineDefinition));
        await locations.Task;
        var known = locations.Status == AsyncOperationStatus.Succeeded && locations.Result.Count > 0;
        locations.Release();
        if (!known)
        {
            Debug.Log($"[Tags] no machine for {address}");
            return null;
        }

        // Kept loaded: a definition is a few hundred bytes and stays in use while the machine is placed.
        var load = Addressables.LoadAssetAsync<MachineDefinition>(address);
        await load.Task;
        return load.Status == AsyncOperationStatus.Succeeded ? load.Result : null;
    }
}

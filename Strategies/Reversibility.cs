using Neo.Json;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;

namespace Neo.Optimizer
{
    public static class Reversibility
    {
        [Strategy(Priority = 0)]
        public static (NefFile, ContractManifest, JToken) RemoveReversibleInstructions(NefFile nef, ContractManifest manifest, JToken debugInfo)
        {
            return (nef, manifest, debugInfo);
        }
    }
}

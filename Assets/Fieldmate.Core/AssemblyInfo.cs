using System.Runtime.CompilerServices;

// Fieldmate.Core: engine-light domain code (twin, procedures, knowledge, AI, vision).
// Rule (design.md §5.2): no UnityEngine types beyond math, no XR packages, no vendor SDKs.
[assembly: InternalsVisibleTo("Fieldmate.Tests.EditMode")]
[assembly: InternalsVisibleTo("Fieldmate.Tests.PlayMode")]

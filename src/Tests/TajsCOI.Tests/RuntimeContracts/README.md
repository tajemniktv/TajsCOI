# Runtime contracts

The tests in this directory are intentionally separate from pure algorithm tests. They validate the exact Captain of
Industry 0.8.7b seams that TajsCOI consumes: managed type/member signatures, Harmony target discovery, command
payload/action routes, profiler event attribution, and process-lifetime patch ownership.

They are fast tests. They load the managed assemblies referenced by `COI_ROOT`, but they do not start Unity, create a
gameplay resolver, load a save, or benchmark pathfinding. A failure names the expected signature and includes the loaded
assembly context so game-version drift is noisy.

Run the contracts with the normal test project (from the repository root):

```powershell
$env:COI_ROOT = 'E:\dev\CaptainOfIndustry\TajsCOI-Refs\refs\0.8.7b'
dotnet test TajsCOI.slnx --configuration Debug --no-restore -m:1 -nr:false --filter FullyQualifiedName~RuntimeContracts
```

The process-lifetime Harmony test always removes its test owner in `finally`. Full gameplay, save/reload, Unity UI, and
performance A/B validation remain integration/manual work; these tests only protect the contracts that can be checked
without a production save.

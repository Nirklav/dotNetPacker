# dotNetPacker
Packs dll and exe into one exe file.

## Help

Required parameters:
1. assemblyName - (parameter) Output executing assembly name (without extension).
2. inputAssemblies - (list) Assemblues that will be merget. Last of this assemblies must be executable.
3. fileKind - (parameter) Result executable type: ConsoleApplication, WindowApplication.
  
  
## Example:

  packer.exe assemblyName SomeName inputAssemblies one.dll,two.dll,app.exe fileKind WindowApplication

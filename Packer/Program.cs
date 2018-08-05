using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

namespace Packer
{
  class Program
  {
    public static void Main(string[] args)
    {
      try
      {
        var argsParser = new ArgsParser(args);
        var assemblyName = argsParser.Get("assemblyName");
        var inputAssemblies = argsParser.GetAll("inputAssemblies");
        var fileKind = argsParser.Get("fileKind", (string s, out PEFileKinds r) => Enum.TryParse(s, out r));

        if (fileKind == PEFileKinds.Dll)
          throw new ArgumentException("File kind must be equals only ConsoleApplication or WindowApplication");

        CreateAssembly(assemblyName, inputAssemblies, fileKind);
      }
      catch (Exception e)
      {
        PrintHelp();

        Console.WriteLine();
        Console.WriteLine(e);
      }
    }

    private static void PrintHelp()
    {
      Console.WriteLine(@"
Required parameters:
  assemblyName - (parameter) Output executing assembly name (without extension).
  inputAssemblies - (list) Assemblues that will be merget. Last of this assemblies must be executable.
  fileKind - (parameter) Result executable type: ConsoleApplication, WindowApplication.

Example:
  packer.exe assemblyName SomeName inputAssemblies one.dll,two.dll,app.exe fileKind WindowApplication");
    }

    private static void CreateAssembly(string assemblyName, string[] inputAssemblies, PEFileKinds fileKind)
    {
      var outputFileName = $"{assemblyName}.exe";
      var builder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Save);
      var moduleBuilder = builder.DefineDynamicModule("main-module", outputFileName);

      var resourceNames = new List<string>();

      var counter = 0;
      var fileStreams = new List<Stream>();
      foreach (var filePath in inputAssemblies)
      {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var resourceName = $"{fileName}_asm_{counter++:0000}";

        resourceNames.Add(resourceName);
        var fileStream = File.OpenRead(filePath);
        fileStreams.Add(fileStream);
        moduleBuilder.DefineManifestResource(resourceName, fileStream, ResourceAttributes.Private);
      }

      var typeBuilder = moduleBuilder.DefineType("Program", TypeAttributes.Class | TypeAttributes.Public);
      var assembliesField = typeBuilder.DefineField("_assemblies", typeof(List<Assembly>), FieldAttributes.Private | FieldAttributes.Static);

      // Assemblies resolve method
      var assemblyResolve = GenerateAssemblyResolve(typeBuilder, assembliesField);

      // Main method
      var main = GenerateMain(typeBuilder, assembliesField, assemblyResolve, resourceNames);

      typeBuilder.CreateType();
      builder.SetEntryPoint(main, fileKind);
      builder.Save(outputFileName);

      foreach (var stream in fileStreams)
        stream.Dispose();
    }

    private static MethodInfo GenerateAssemblyResolve(TypeBuilder typeBuilder, FieldInfo assemliesField)
    {
      var methodBuilder = typeBuilder.DefineMethod("Resolve", MethodAttributes.Public | MethodAttributes.Static, typeof(Assembly), new[] { typeof(object), typeof(ResolveEventArgs) });

      var il = methodBuilder.GetILGenerator();
      var success = il.DefineLabel();
      var cycle = il.DefineLabel();

      il.DeclareLocal(typeof(int));
      il.DeclareLocal(typeof(Assembly));

      il.Emit(OpCodes.Ldsfld, assemliesField);
      il.Emit(OpCodes.Callvirt, typeof(List<Assembly>).GetProperty(nameof(List<Assembly>.Count)).GetMethod);
      il.Emit(OpCodes.Stloc_0);

      // Decrement
      il.MarkLabel(cycle);
      il.Emit(OpCodes.Ldloc_0);
      il.Emit(OpCodes.Ldc_I4, -1);
      il.Emit(OpCodes.Add);
      il.Emit(OpCodes.Stloc_0);

      // Load item
      il.Emit(OpCodes.Ldsfld, assemliesField);
      il.Emit(OpCodes.Ldloc_0);
      il.Emit(OpCodes.Callvirt, typeof(List<Assembly>).GetMethod("get_Item"));
      il.Emit(OpCodes.Stloc_1);

      // Create required assemblyName and get simple name
      il.Emit(OpCodes.Ldarg_1);
      il.Emit(OpCodes.Callvirt, typeof(ResolveEventArgs).GetProperty(nameof(ResolveEventArgs.Name)).GetMethod);
      il.Emit(OpCodes.Newobj, typeof(AssemblyName).GetConstructor(new[] { typeof(string) }));
      il.Emit(OpCodes.Callvirt, typeof(AssemblyName).GetProperty(nameof(AssemblyName.Name)).GetMethod);

      // Create existing assemblyName and get simple name
      il.Emit(OpCodes.Ldloc_1);
      il.Emit(OpCodes.Callvirt, typeof(Assembly).GetProperty(nameof(Assembly.FullName)).GetMethod);
      il.Emit(OpCodes.Newobj, typeof(AssemblyName).GetConstructor(new[] { typeof(string) }));
      il.Emit(OpCodes.Callvirt, typeof(AssemblyName).GetProperty(nameof(AssemblyName.Name)).GetMethod);

      // Compare names
      il.Emit(OpCodes.Call, typeof(string).GetMethod(nameof(string.Equals), new[] { typeof(string), typeof(string) }));
      il.Emit(OpCodes.Brtrue, success);

      // Go to cycle
      il.Emit(OpCodes.Ldloc_0);
      il.Emit(OpCodes.Brtrue, cycle);

      // False return
      il.Emit(OpCodes.Ldnull);
      il.Emit(OpCodes.Ret);

      // True return
      il.MarkLabel(success);
      il.Emit(OpCodes.Ldloc_1);
      il.Emit(OpCodes.Ret);

      return methodBuilder;
    }

    private static MethodInfo GenerateMain(TypeBuilder typeBuilder, FieldInfo assembliesField, MethodInfo assemblyResolve, List<string> resourceNames)
    {
      var methodBuilder = typeBuilder.DefineMethod("Main", MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Static, typeof(void), new[] { typeof(string[]) });
      methodBuilder.SetCustomAttribute(new CustomAttributeBuilder(typeof(STAThreadAttribute).GetConstructor(Type.EmptyTypes), new object[0]));

      var il = methodBuilder.GetILGenerator();
      il.DeclareLocal(typeof(Assembly));
      il.DeclareLocal(typeof(Stream));
      il.DeclareLocal(typeof(byte[]));

      // Create field
      il.Emit(OpCodes.Newobj, typeof(List<Assembly>).GetConstructor(Type.EmptyTypes));
      il.Emit(OpCodes.Stsfld, assembliesField);

      // Subscribe to assembly resolve
      il.Emit(OpCodes.Call, typeof(AppDomain).GetProperty(nameof(AppDomain.CurrentDomain)).GetMethod);
      il.Emit(OpCodes.Ldnull);
      il.Emit(OpCodes.Ldftn, assemblyResolve);
      il.Emit(OpCodes.Newobj, typeof(ResolveEventHandler).GetConstructor(new[] { typeof(object), typeof(IntPtr) }));
      il.Emit(OpCodes.Callvirt, typeof(AppDomain).GetEvent(nameof(AppDomain.AssemblyResolve)).AddMethod);

      // Set current assembly local
      il.Emit(OpCodes.Call, typeof(Assembly).GetMethod(nameof(Assembly.GetExecutingAssembly)));
      il.Emit(OpCodes.Stloc_0);

      for (var i = 0; i < resourceNames.Count; i++)
      {
        var resourceName = resourceNames[i];
        var isExecutable = resourceNames.Count - 1 == i;

        // Call GetManifestResourceStream and save rsult to local
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ldstr, resourceName);
        il.Emit(OpCodes.Callvirt, typeof(Assembly).GetMethod(nameof(Assembly.GetManifestResourceStream), new[] { typeof(string) }));
        il.Emit(OpCodes.Stloc_1);

        // Create array
        il.Emit(OpCodes.Ldloc_1);
        il.Emit(OpCodes.Callvirt, typeof(Stream).GetProperty(nameof(Stream.Length)).GetMethod);
        il.Emit(OpCodes.Newarr, typeof(byte));
        il.Emit(OpCodes.Stloc_2);

        // Read to array
        il.Emit(OpCodes.Ldloc_1);
        il.Emit(OpCodes.Ldloc_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc_2);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Callvirt, typeof(Stream).GetMethod(nameof(Stream.Read), new[] { typeof(byte[]), typeof(int), typeof(int) }));
        il.Emit(OpCodes.Pop); // Remove Read method result

        if (!isExecutable)
        {
          // Prepare to add to list
          il.Emit(OpCodes.Ldsfld, assembliesField);
        }

        // Load assembly
        il.Emit(OpCodes.Ldloc_2);
        il.Emit(OpCodes.Call, typeof(Assembly).GetMethod(nameof(Assembly.Load), new[] { typeof(byte[]) }));

        if (!isExecutable)
        {
          // Add to list
          il.Emit(OpCodes.Callvirt, typeof(List<Assembly>).GetMethod(nameof(List<Assembly>.Add)));
        }
        else
        {
          il.Emit(OpCodes.Callvirt, typeof(Assembly).GetProperty(nameof(Assembly.EntryPoint)).GetMethod);
          il.Emit(OpCodes.Ldnull);
          il.Emit(OpCodes.Ldc_I4_0);
          il.Emit(OpCodes.Newarr, typeof(string));
          il.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetMethod(nameof(MethodInfo.Invoke), new[] { typeof(object), typeof(object[]) }));
          il.Emit(OpCodes.Pop);
        }
      }

      il.Emit(OpCodes.Ret);

      return methodBuilder;
    }
  }
}

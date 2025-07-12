using NativeRoutines;
using System.Reflection;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

var domain = AppDomain.CurrentDomain;
domain.AssemblyResolve += (sender, args) =>
{
    // This is where we can load the assembly from a specific path
    // For example, if you have the assembly in a specific directory
    var assemblyName = new AssemblyName("NativeRoutines.dll");
    var assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName.Name}.dll");
    
    if (File.Exists(assemblyPath))
    {
        return Assembly.LoadFrom(assemblyPath);
    }
    
    return null; // or throw an exception if you want to handle it differently
}
;
//var currentPath = Environment.CurrentDirectory;

//var desiredPath = (Path.Combine(currentPath, @"runtimes\win10-arm64\lib\net9.0\NativeRoutines.dll"));
//Environment.CurrentDirectory = Path.GetDirectoryName(desiredPath) ?? currentPath;

//var checkPath = Environment.CurrentDirectory;
//byte[] rawAssembly = loadFile("NativeRoutines.dll");
//byte[] rawAssembly = loadFile(@"C:\temp\GFS-Fresh_Release_Build_From_VS22\net9.0-windows\NativeRoutines.dll");

//byte[] rawSymbolStore = loadFile("temp.pdb");
//Assembly assembly = domain.Load(rawAssembly, null);

var ourExeName = NativeRoutines.PrintRoutines.GetOurExeName();

//var ourExeName = assembly.GetName().Name + ".exe";
Console.WriteLine($"Running executable '{ourExeName}'. Press any key to exit...");
Console.ReadKey();


// Loads the content of a file to a byte array.
static byte[] loadFile(string filename)
{
    FileStream fs = new FileStream(filename, FileMode.Open);
    byte[] buffer = new byte[(int)fs.Length];
    fs.Read(buffer, 0, buffer.Length);
    fs.Close();

    return buffer;
}



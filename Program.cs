namespace S3KOHashCrack;
using RSDKv5;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

static partial class Program
{
    static readonly string[] defaultEntityVarNames = new string[] {
        "position",
        "scale",
        "velocity",
        "updateRange",
        "angle",
        "alpha",
        "rotation",
        "groundVel",
        "zdepth",
        "group",
        "classID",
        "inRange",
        "isPermanent",
        "tileCollisions",
        "interaction",
        "onGround",
        "active",
        "filter",
        "direction",
        "drawGroup",
        "collisionLayers",
        "collisionPlane",
        "collisionMode",
        "drawFX",
        "inkEffect",
        "visible",
        "onScreen",
    };

    // this used to concatenate a much longer list of names while searching
    // now that most of the names are found we can use a much smaller list :>
    static readonly List<string> namesLookup = File.ReadAllLines("filenames.txt")
    .Concat(defaultEntityVarNames)
    .Concat(File.ReadAllLines("varnames.txt"))
    .Concat(File.ReadAllLines("objectnames.txt"))
    // .Concat(File.ReadAllLines("objecthashlookup.txt"))
    // .Concat(File.ReadAllLines("ghidra_strings.txt"))
    // .Concat(File.ReadAllLines("origins_strings.txt"))
    .Distinct()
    .ToList();

    static void Main(string[] args)
    {
        // timer is just for perf measuring
        var timer = Stopwatch.StartNew();

        // load our data file
        using var dataFileStream = File.OpenRead("Data.rsdk");
        var dataPack = new DataPack(dataFileStream, namesLookup);

        // get the GameConfig from the data file
        using var gameConfigStream = new MemoryStream(dataPack.files.Single(f => f.name.name.EndsWith("GameConfig.bin")).data);
        var gameConfig = new GameConfig(gameConfigStream);

        // add object names known to exist to lookup
        foreach (var objName in gameConfig.objects)
        {
            if (!namesLookup.Contains(objName))
            {
                namesLookup.Add(objName);
            }
        }

        // some setup
        var objNames = new List<string>();
        var varNames = new List<string>();
        var unresolvedSymbols = new HashSet<string>();
        var builder = new StringBuilder();
        builder.EnsureCapacity(1_000);

        // file for writing decomp-style c structs
        using var structStream = File.CreateText("entities.txt");

        // iterate through each scene in each category
        foreach (var cat in gameConfig.categories)
        {
            foreach (var si in cat.list)
            {
                // get file from info in GameConfig
                // we use FirstOrDefault and check for null
                // because scenes like The Zoo can be listed but don't exist
                var file = dataPack.files.FirstOrDefault(f => f.name.name == $"Data/Stages/{si.folder}/Scene{si.id}.bin");
                if (file == null)
                {
                    continue;
                }

                // actually load the scene (this can take some time due to the huge lookup being used)
                Console.WriteLine($"Loading scene {cat.name}/{si.name} ({file.name.name})");
                using var sceneStream = new MemoryStream(file.data);
                var scene = new Scene(sceneStream, namesLookup, namesLookup);

                // iterate through each object in the scene
                foreach (var obj in scene.objects)
                {
                    // the huge lookup + reading from GameConfig should've resolved the name
                    // if for some reason we still don't have it, report it
                    if (obj.name.usingHash)
                    {
                        Console.WriteLine($"Unknown object name {obj.name.HashString()}");
                        unresolvedSymbols.Add(obj.name.HashString());
                    }

                    // skip writing the struct for this object if we've already looked at it
                    if (objNames.Contains(obj.name.name ?? obj.name.HashString()))
                    {
                        continue;
                    }

                    objNames.Add(obj.name.name ?? obj.name.HashString());

                    // start writing string for struct representation
                    builder.Clear();
                    builder.AppendLine($"struct Entity{obj.name} {{\n\tRSDK_ENTITY");

                    // iterate through every editable variable in the object
                    foreach (var vi in obj.variables)
                    {
                        // report if we don't know the actual name of the variable
                        if (vi.name.usingHash)
                        {
                            Console.WriteLine($"Unknown variable name {vi.name.HashString()}");
                            unresolvedSymbols.Add(vi.name.HashString());
                        }

                        varNames.Add(vi.name.name ?? vi.name.HashString());

                        // skip variables held by RSDK_ENTITY when printing out struct
                        if (defaultEntityVarNames.Contains(vi.name.name))
                        {
                            continue;
                        }

                        // write variable to struct representation
                        builder.AppendLine($"\t{vi.type switch
                        {
                            VariableTypes.Enum => "int32",
                            VariableTypes.Float => "float",
                            VariableTypes t => t.ToString()
                        }} {vi.name};");
                    }

                    // done writing struct for object
                    builder.AppendLine("};");
                    structStream.WriteLine(builder.ToString());
                    structStream.Flush();
                }
            }
        }

        // debugging stuff. writes out only the used symbols from a bigger lookup to files
        /*
        // write all unique object names (including unknown hashes) to a file
        using (var objNamesFile = File.OpenWrite("objectnames.txt"))
        {
            using var writer = new StreamWriter(objNamesFile);
            foreach (var objName in objNames.Distinct().Order())
            {
                writer.WriteLine(objName);
            }
        }

        // write all unique variable names (including unknown hashes) to a file
        using (var varNamesFile = File.OpenWrite("varnames.txt"))
        {
            using var writer = new StreamWriter(varNamesFile);
            foreach (var varName in varNames.Distinct().Order())
            {
                writer.WriteLine(varName);
            }
        }
        */

        timer.Stop();
        Console.WriteLine($"Done in {timer.Elapsed.TotalSeconds}s");

        // how did we do?

        Console.WriteLine($"{objNames.Count} objects");
        Console.WriteLine($"{varNames.Distinct().Count()} unique variable names");

        if (unresolvedSymbols.Count > 0)
        {
            Console.WriteLine($"Unresolved symbols ({unresolvedSymbols.Count}):");
            foreach (var unresolved in unresolvedSymbols)
            {
                Console.WriteLine(unresolved);
            }
        }
    }
}
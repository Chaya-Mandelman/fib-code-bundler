

using System.CommandLine;
using System.Text;

var rootCommand = new RootCommand("fib - A tool for bundling code files");

var bundleCommand = new Command("bundle", "Bundle code files into a single file");

var languageOption = new Option<string[]>("--language", "-l")
{
    Description = "Languages to include, or 'all' for all supported languages",
    Required = true,
    AllowMultipleArgumentsPerToken = true
};

var outputOption = new Option<string>("--output", "-o")
{
    Description = "Output file name or path",
    Required = true
};

var noteOption = new Option<bool>("--note", "-n")
{
    Description = "Add a comment indicating the source of each file"
};

var sortOption = new Option<string>("--sort", "-s")
{
    Description = "File sorting order: name or type",
    DefaultValueFactory = _ => "name"
};

var removeEmptyLinesOption =
    new Option<bool>("--remove-empty-lines", "-r")
    {
        Description = "Remove empty lines"
    };

var authorOption = new Option<string?>("--author", "-a")
{
    Description = "Name of the bundle author"
};

bundleCommand.Options.Add(languageOption);
bundleCommand.Options.Add(outputOption);
bundleCommand.Options.Add(noteOption);
bundleCommand.Options.Add(sortOption);
bundleCommand.Options.Add(removeEmptyLinesOption);
bundleCommand.Options.Add(authorOption);

bundleCommand.SetAction(parseResult =>
{
    var languages = parseResult.GetValue(languageOption) ?? Array.Empty<string>();
    var output = parseResult.GetValue(outputOption);
    var note = parseResult.GetValue(noteOption);
    var sort = parseResult.GetValue(sortOption) ?? "name";
    var removeEmpty = parseResult.GetValue(removeEmptyLinesOption);
    var author = parseResult.GetValue(authorOption);

    return Bundle(languages, output, note, sort, removeEmpty, author) ? 0 : 1;
});

var createRspCommand = new Command(
    "create-rsp",
    "Create a response file and run the bundling process");

createRspCommand.SetAction(_ =>
{
    return CreateRspAndRun(bundleCommand) ? 0 : 1;
});

rootCommand.Subcommands.Add(bundleCommand);
rootCommand.Subcommands.Add(createRspCommand);

return rootCommand.Parse(args).Invoke();


// ======================================================
// Language configuration and file bundling
// ======================================================

static bool Bundle(
    string[] requestedLanguages,
    string outputPath,
    bool includeNotes,
    string sort,
    bool removeEmptyLines,
    string? author)
{
    var supportedExtensions = new Dictionary<string, string[]>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] = new[] { ".cs" },
        ["csharp"] = new[] { ".cs" },
        ["py"] = new[] { ".py" },
        ["python"] = new[] { ".py" },
        ["js"] = new[] { ".js", ".jsx" },
        ["javascript"] = new[] { ".js", ".jsx" },
        ["ts"] = new[] { ".ts", ".tsx" },
        ["typescript"] = new[] { ".ts", ".tsx" },
        ["java"] = new[] { ".java" },
        ["c"] = new[] { ".c", ".h" },
        ["cpp"] = new[] { ".cpp", ".hpp", ".cc", ".hh" },
        ["html"] = new[] { ".html", ".htm" },
        ["css"] = new[] { ".css" },
        ["sql"] = new[] { ".sql" },
        ["json"] = new[] { ".json" },
        ["xml"] = new[] { ".xml" },
        ["go"] = new[] { ".go" },
        ["rust"] = new[] { ".rs" },
        ["php"] = new[] { ".php" },
        ["ruby"] = new[] { ".rb" }
    };

    if (requestedLanguages.Length == 0)
    {
        Console.Error.WriteLine(
            "Error: Specify at least one language using --language.");
        return false;
    }

    var languages = requestedLanguages
        .SelectMany(x => x.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries))
        .Select(x => x.ToLowerInvariant())
        .ToArray();

    if (languages.Contains("all") && languages.Length > 1)
    {
        Console.Error.WriteLine(
            "Error: 'all' cannot be combined with other language names.");
        return false;
    }

    var unknownLanguages = languages
        .Where(x => x != "all" && !supportedExtensions.ContainsKey(x))
        .Distinct()
        .ToArray();

    if (unknownLanguages.Length > 0)
    {
        Console.Error.WriteLine(
            $"Error: Unsupported language: {string.Join(", ", unknownLanguages)}.");

        Console.Error.WriteLine(
            $"Supported languages: {string.Join(", ", supportedExtensions.Keys.Distinct().OrderBy(x => x))}, all.");

        return false;
    }

    if (!sort.Equals("name", StringComparison.OrdinalIgnoreCase) &&
        !sort.Equals("type", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            "Error: The sort value must be name or type.");
        return false;
    }

    if (string.IsNullOrWhiteSpace(outputPath))
    {
        Console.Error.WriteLine("Error: An output path must be specified.");
        return false;
    }

    try
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var fullOutputPath = Path.GetFullPath(outputPath, currentDirectory);
        var outputDirectory = Path.GetDirectoryName(fullOutputPath);

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            Console.Error.WriteLine("Error: Unable to determine the output directory.");
            return false;
        }

        Directory.CreateDirectory(outputDirectory);

        var ignoredDirectories = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "bin",
            "obj",
            "debug",
            "release",
            ".git",
            ".vs",
            ".idea",
            ".vscode",
            "node_modules",
            "publish"
        };

        var selectedExtensions = languages.Contains("all")
            ? supportedExtensions.Values
                .SelectMany(x => x)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : languages
                .SelectMany(x => supportedExtensions[x])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var files = Directory
            .EnumerateFiles(
                currentDirectory,
                "*",
                SearchOption.AllDirectories)
            .Where(file =>
            {
                var fullPath = Path.GetFullPath(file);

                // Exclude the output file to prevent it from being included in itself.
                if (string.Equals(
                    fullPath,
                    fullOutputPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var relativePath = Path.GetRelativePath(
                    currentDirectory,
                    fullPath);

                var parts = relativePath.Split(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

                // Ignore directories that are not part of the source code to be submitted.
                if (parts.Any(part => ignoredDirectories.Contains(part)))
                    return false;

                return selectedExtensions.Contains(Path.GetExtension(file));
            });

        if (sort.Equals("type", StringComparison.OrdinalIgnoreCase))
        {
            files = files
                .OrderBy(Path.GetExtension, StringComparer.OrdinalIgnoreCase)
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            files = files
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    file => Path.GetRelativePath(currentDirectory, file),
                    StringComparer.OrdinalIgnoreCase);
        }

        var fileList = files.ToList();

        using var writer = new StreamWriter(
            fullOutputPath,
            false,
            new UTF8Encoding(false));

        if (!string.IsNullOrWhiteSpace(author))
        {
            writer.WriteLine($"// Author: {author.Trim()}");
            writer.WriteLine();
        }

        foreach (var file in fileList)
        {
            if (includeNotes)
            {
                var relativePath = Path.GetRelativePath(
                    currentDirectory,
                    file);

                writer.WriteLine($"// Source: {relativePath}");
            }

            var lines = File.ReadAllLines(file);

            foreach (var line in lines)
            {
                if (removeEmptyLines && string.IsNullOrWhiteSpace(line))
                    continue;

                writer.WriteLine(line);
            }

            writer.WriteLine();
        }

        Console.WriteLine(
            $"Bundling completed successfully. Number of files: {fileList.Count}");

        Console.WriteLine($"Output file: {fullOutputPath}");
        return true;
    }
    catch (UnauthorizedAccessException ex)
    {
        Console.Error.WriteLine(
            $"Permission error: Unable to read or write a file. {ex.Message}");
        return false;
    }
    catch (DirectoryNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: Directory not found. {ex.Message}");
        return false;
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"Input/output error: {ex.Message}");
        return false;
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine($"Invalid path or file name: {ex.Message}");
        return false;
    }
    catch (NotSupportedException ex)
    {
        Console.Error.WriteLine($"Unsupported path: {ex.Message}");
        return false;
    }
}


// ======================================================
// create-rsp: Collect options, create a response file, and run bundling
// ======================================================

static bool CreateRspAndRun(Command bundleCommand)
{
    Console.WriteLine("Create a response file for code bundling.");
    Console.WriteLine();

    var languages = AskLanguages();
    var output = AskNonEmpty("Output file name or path: ");
    var includeNotes = AskYesNo("Add a comment indicating the source of each file? (y/n): ");
    var sort = AskSort();
    var removeEmptyLines = AskYesNo("Remove empty lines? (y/n): ");
    var author = AskOptional("Author name (leave blank if not applicable): ");
    var rspPath = "stam.rsp";

    var arguments = new List<string>
    {
        "--language"
    };

    arguments.AddRange(languages);
    arguments.Add("--output");
    arguments.Add(output);

    if (includeNotes)
        arguments.Add("--note");

    arguments.Add("--sort");
    arguments.Add(sort);

    if (removeEmptyLines)
        arguments.Add("--remove-empty-lines");

    if (!string.IsNullOrWhiteSpace(author))
    {
        arguments.Add("--author");
        arguments.Add(author);
    }

    try
    {
        // The response file must also include the subcommand name.
        var responseTokens = new List<string> { "bundle" };
        responseTokens.AddRange(arguments);

        var responseContent = string.Join(
            Environment.NewLine,
            responseTokens.Select(QuoteArgument));

        File.WriteAllText(
            rspPath,
            responseContent + Environment.NewLine,
            new UTF8Encoding(false));

        
        // Execute the command using the collected arguments.
        var parseResult = bundleCommand.Parse(arguments.ToArray());
        var exitCode = parseResult.Invoke();

        if (exitCode == 0)
        {
            File.Delete(rspPath);
            Console.WriteLine("Bundling succeeded.");
            return true;
        }

        Console.Error.WriteLine("Bundling failed.");
        Console.Error.WriteLine(
            $"The response file was preserved for inspection and rerunning: {Path.GetFullPath(rspPath)}");

        return false;
    }
    catch (UnauthorizedAccessException ex)
    {
        Console.Error.WriteLine(
            $"Permission denied while creating or deleting the response file: {ex.Message}");
        return false;
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine(
            $"Error while working with the response file: {ex.Message}");
        return false;
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine($"Invalid name or path: {ex.Message}");
        return false;
    }
}


// ======================================================
// Helper functions for collecting input with retry support
// ======================================================

static string[] AskLanguages()
{
    var supported = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "cs", "csharp",
        "py", "python",
        "js", "javascript",
        "ts", "typescript",
        "java", "c", "cpp",
        "html", "css", "sql", "json", "xml",
        "go", "rust", "php", "ruby",
        "all"
    };

    while (true)
    {
        Console.Write(
            "Programming languages (e.g., cs or py js; you can also enter all): ");

        var input = Console.ReadLine() ?? "";

        var values = input
            .Split(
                new[] { ' ', ',', ';' },
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .ToArray();

        if (values.Length == 0)
        {
            Console.WriteLine(
                "No language was entered. Enter at least one language.");
            continue;
        }

        if (values.Contains("all") && values.Length > 1)
        {
            Console.WriteLine(
                "You cannot combine all with other languages. Enter all by itself or select specific languages.");
            continue;
        }

        var invalid = values
            .Where(x => !supported.Contains(x))
            .Distinct()
            .ToArray();

        if (invalid.Length > 0)
        {
            Console.WriteLine(
                $"Unknown language: {string.Join(", ", invalid)}. Please try again.");
            continue;
        }

        return values;
    }
}

static string AskNonEmpty(string prompt)
{
    while (true)
    {
        Console.Write(prompt);
        var value = Console.ReadLine()?.Trim() ?? "";

        if (!string.IsNullOrWhiteSpace(value))
            return value;

        Console.WriteLine(
            "The value cannot be empty. Please try again.");
    }
}

static string AskOptional(string prompt)
{
    Console.Write(prompt);
    return Console.ReadLine()?.Trim() ?? "";
}

static bool AskYesNo(string prompt)
{
    while (true)
    {
        Console.Write(prompt);

        var value = (Console.ReadLine() ?? "")
            .Trim()
            .ToLowerInvariant();

        if (value is "y" or "yes" or "כן")
            return true;

        if (value is "n" or "no" or "לא")
            return false;

        Console.WriteLine(
            "Invalid response. Enter y or n, or כן or לא.");
    }
}

static string AskSort()
{
    while (true)
    {
        Console.Write(
            "File sorting order (name/type, default: name): ");

        var value = (Console.ReadLine() ?? "").Trim();

        if (value.Length == 0)
            return "name";

        if (value.Equals("name", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("type", StringComparison.OrdinalIgnoreCase))
        {
            return value.ToLowerInvariant();
        }

        Console.WriteLine(
            "Invalid value. Enter name or type. Please try again.");
    }
}


static string QuoteArgument(string argument)
{
    // Preserve spaces within a single argument in the response file.
    return "\"" +
           argument.Replace("\\", "\\\\").Replace("\"", "\\\"") +
           "\"";
}


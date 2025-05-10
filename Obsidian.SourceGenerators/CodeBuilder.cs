using Obsidian.SourceGenerators.Registry;
using System.Text;
using System.Text.Json;

namespace Obsidian.SourceGenerators;

public sealed class CodeBuilder
{
    public int Indentation => _indent;
    private int _indent = 0;

    private readonly StringBuilder builder = new();

    private const string indentationString = "    ";

    public CodeBuilder Indent()
    {
        for (int i = 0; i < _indent; i++)
            builder.Append(indentationString);
        return this;
    }

    public CodeBuilder Append(string text)
    {
        builder.Append(text);
        return this;
    }

    public CodeBuilder Append(CodeBuilder other)
    {
        builder.Append(other.ToString());
        return this;
    }

    public CodeBuilder Comment(string text)
    {
        return AppendLine($"// {text}");
    }

    public CodeBuilder Using(string library)
    {
        return AppendLine($"using {library};");
    }

    public CodeBuilder StaticUsing(string library) =>
        AppendLine($"using static {library};");

    public CodeBuilder Namespace(string name)
    {
        return AppendLine($"namespace {name};");
    }

    public CodeBuilder Type(string signature)
    {
        return AppendScoped(signature);
    }

    public CodeBuilder Array(string signature) => AppendArrayScoped(signature);

    public CodeBuilder Type(INamedTypeSymbol typeSymbol)
    {
        var declaration = new StringBuilder();

        declaration.Append(typeSymbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            _ => throw new Exception("Unsupported accessibility modifier")
        });

        declaration.Append(' ');

        if (typeSymbol.IsAbstract)
            declaration.Append("abstract ");

        if (typeSymbol.IsSealed)
            declaration.Append("sealed ");

        if (typeSymbol.IsStatic)
            declaration.Append("static ");

        if (typeSymbol.IsReadOnly)
            declaration.Append("readonly ");

        declaration.Append("partial ");

        declaration.Append(typeSymbol.TypeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            TypeKind.Enum => "enum",
            TypeKind.Delegate => "delegate",
            _ => throw new Exception("Unsupported type kind")
        });

        declaration.Append(' ');

        declaration.Append(typeSymbol.Name);

        if (typeSymbol.IsGenericType)
        {
            declaration.Append('<');
            declaration.Append(string.Join(", ", typeSymbol.TypeArguments.Select(arg => arg.Name)));
            declaration.Append('>');
        }

        return AppendScoped(declaration.ToString());
    }

    public CodeBuilder Method(string signature)
    {
        return AppendScoped(signature);
    }

    public CodeBuilder Statement(string statement)
    {
        return AppendScoped(statement);
    }

    public CodeBuilder Line()
    {
        builder.AppendLine();
        return this;
    }

    public CodeBuilder AppendSimple(string code, bool newLine) => newLine ? this.Line(code) : this.Append(code);

    public CodeBuilder Line(string code)
    {
        return AppendLine(code);
    }

    public CodeBuilder EndScope()
    {
        _indent--;
        AppendLine("}");
        return this;
    }

    public CodeBuilder EndScope(bool semicolon)
    {
        _indent--;
        if (semicolon)
        {
            AppendLine("};");
        }
        else
        {
            AppendLine("}");
        }
        return this;
    }

    public CodeBuilder EndScope(string code, bool semicolon)
    {
        _indent--;
        if (semicolon)
        {
            AppendLine($"}}{code};");
        }
        else
        {
            AppendLine($"}}{code}");
        }
        return this;
    }

    public CodeBuilder EndArrayScope(bool semicolon)
    {
        _indent--;
        if (semicolon)
        {
            AppendLine("];");
        }
        else
        {
            AppendLine("]");
        }
        return this;
    }

    public CodeBuilder EndArrayScope(string code, bool semicolon)
    {
        _indent--;
        if (semicolon)
        {
            AppendLine($"]{code};");
        }
        else
        {
            AppendLine($"]{code}");
        }
        return this;
    }

    public CodeBuilder EndArrayScope()
    {
        _indent--;
        AppendLine("]");
        return this;
    }

    public CodeBuilder XmlSummary(string summary)
    {
        return Xml("summary", summary, inline: false);
    }

    public CodeBuilder XmlParam(string param, string description)
    {
        return Xml("param", @$"name=""{param}""", description, inline: true);
    }

    public CodeBuilder XmlReturns(string description)
    {
        return Xml("returns", description, inline: true);
    }

    public CodeBuilder Clear()
    {
        _indent = 0;
        builder.Clear();
        return this;
    }

    private CodeBuilder AppendLine(string line)
    {
        Indent();
        builder.AppendLine(line);
        return this;
    }

    private CodeBuilder AppendScoped(string line)
    {
        var instance = AppendLine(line).AppendLine("{");
        _indent++;
        return instance;
    }

    private CodeBuilder AppendArrayScoped(string line)
    {
        var instance = AppendLine(line).AppendLine("[");
        _indent++;
        return instance;
    }

    private CodeBuilder Xml(string type, string content, bool inline)
    {
        content = content.Replace("\n", " ");
        if (inline)
        {
            return AppendLine($"/// <{type}>{content}</{type}>");
        }
        else
        {
            return AppendLine($"/// <{type}>")
                  .AppendLine($"/// {content}")
                  .AppendLine($"/// </{type}>");
        }
    }

    private CodeBuilder Xml(string type, string attributes, string content, bool inline)
    {
        content = content.Replace("\n", " ");
        if (inline)
        {
            return AppendLine($"/// <{type} {attributes}>{content}</{type}>");
        }
        else
        {
            return AppendLine($"/// <{type} {attributes}>")
                  .AppendLine($"/// {content}")
                  .AppendLine($"/// </{type}>");
        }
    }

    public static CodeBuilder WithIndentationOf(CodeBuilder codeBuilder)
    {
        return new CodeBuilder
        {
            _indent = codeBuilder._indent
        };
    }

    public static CodeBuilder WithIndentationOf(int indentation)
    {
        return new CodeBuilder
        {
            _indent = indentation
        };
    }

    public override string ToString()
    {
        return builder.ToString();
    }
}

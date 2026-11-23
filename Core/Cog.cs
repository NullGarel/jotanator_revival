// VIBE CODED DSICLAIMER - CLAUDE GENERATED AS OF 26-11-20 @ 22:42 UTC
namespace Jotanator;

/// <summary>Put on a cog class to stop the loader from picking it up (like a leading "_" in the Python version).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DisabledAttribute : Attribute { }

/// <summary>
/// A group of commands. Every non-abstract, non-[Disabled] subclass in the assembly is loaded automatically.
/// Name is the class name without the "Cog" suffix and is used as the section title in the help list.
/// </summary>
public abstract class Cog
{
    public JotaBot Bot { get; internal set; } = null!;
    public IReadOnlyList<CommandDef> Commands { get; internal set; } = Array.Empty<CommandDef>();

    public virtual string Name
    {
        get
        {
            var n = GetType().Name;
            return n.EndsWith("Cog", StringComparison.Ordinal) ? n[..^3] : n;
        }
    }

    public abstract IEnumerable<CommandDef> BuildCommands();

    public virtual Task OnLoadAsync() => Task.CompletedTask;
    public virtual Task OnUnloadAsync() => Task.CompletedTask;
}

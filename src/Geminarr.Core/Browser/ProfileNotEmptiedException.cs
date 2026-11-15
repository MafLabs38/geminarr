namespace Geminarr.Core.Browser;

/// <summary>The profile could not be emptied entirely: the sentence says how many entries stayed (never a path), and asks the operator to empty the folder by hand.</summary>
public sealed class ProfileNotEmptiedException : IOException
{
    public ProfileNotEmptiedException(int left)
        : base($"{left} entries of the profile folder could not be removed: empty it by hand.")
    {
    }
}

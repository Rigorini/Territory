namespace Territory.Application.Export;

/// <summary>One assignment of a territory; dates are local days in the requested time zone.</summary>
public sealed record S13Assignment(string PublisherName, DateOnly AssignedOn, DateOnly? ReturnedOn);

/// <summary>A territory of the S-13 register: assignments are those started in the service year, oldest first.</summary>
public sealed record S13Territory(int Number, DateOnly? LastReturnedBefore, IReadOnlyList<S13Assignment> Assignments);

public sealed record S13Municipality(string Name, IReadOnlyList<S13Territory> Territories);

/// <summary>Format-independent content of the S-13 register of one service year.</summary>
public sealed record S13Report(string Language, int ServiceYear, IReadOnlyList<S13Municipality> Municipalities);

/// <summary>Fills the official S-13 template of the report language.</summary>
public interface IS13Writer
{
    string ContentType { get; }

    void Write(S13Report report, Stream output);
}

namespace CodebaseExplorer.Domain.Model;

/// <summary>Kinds of cross-element invariant violations of an <see cref="ArchitectureModel"/>.</summary>
public enum ModelViolationCode
{
    /// <summary>An element belongs to another repository than the model.</summary>
    WrongRepository,

    /// <summary>The same element (same id and natural key) occurs more than once.</summary>
    DuplicateElement,

    /// <summary>Two different natural keys hash to the same id.</summary>
    NaturalKeyCollision,

    /// <summary>A referenced project does not exist in the model.</summary>
    MissingProject,

    /// <summary>A referenced module does not exist in the model.</summary>
    MissingModule,

    /// <summary>A referenced containing entity does not exist in the model.</summary>
    MissingContainingEntity,

    /// <summary>A referenced file (manifest, solution, declaration or evidence location) does not exist in the model.</summary>
    MissingFile,

    /// <summary>A relationship endpoint does not exist in the model (with the referenced kind).</summary>
    MissingRelationshipEndpoint,

    /// <summary>Module parents or containing entities form a cycle.</summary>
    ContainmentCycle,

    /// <summary>A file assigned to a project lies outside the project's root directory.</summary>
    FileOutsideProject,
}

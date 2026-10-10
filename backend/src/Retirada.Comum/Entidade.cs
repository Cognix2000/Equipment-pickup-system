namespace Retirada.Comum;

/// <summary>Base das entidades: identidade por Id, não pelos valores.</summary>
public abstract class Entidade
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public override bool Equals(object? obj) =>
        obj is Entidade outra && outra.GetType() == GetType() && outra.Id == Id;

    public override int GetHashCode() => Id.GetHashCode();
}

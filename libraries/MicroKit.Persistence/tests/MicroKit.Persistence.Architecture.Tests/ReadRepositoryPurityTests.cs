namespace MicroKit.Persistence.Architecture.Tests;

public sealed class ReadRepositoryPurityTests
{
    private static bool ExposesIQueryable(Type type)
    {
        var queryableType = typeof(IQueryable<>);
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                   .Select(m => UnwrapReturnType(m.ReturnType))
                   .Any(t => t.IsGenericType && t.GetGenericTypeDefinition() == queryableType);

        static Type UnwrapReturnType(Type t)
        {
            if (!t.IsGenericType) return t;
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(Task<>) || def == typeof(ValueTask<>))
                return t.GetGenericArguments()[0];
            return t;
        }
    }

    // NOTE 1: Core's IReadRepository<T> must not declare any mutation methods.
    [Fact]
    public void IReadRepository_Core_HasNoMutationMethods()
    {
        var forbiddenNames = new[] { "CommitAsync", "AddAsync", "UpdateAsync", "DeleteAsync", "SaveChangesAsync" };
        var methods = typeof(MicroKit.Persistence.IReadRepository<>).GetMethods();
        var mutationMethods = methods.Where(m => forbiddenNames.Contains(m.Name)).ToList();
        Assert.Empty(mutationMethods);
    }

    // Open generic form typeof(EfReadRepository<,>) used — two type params.
    [Fact]
    public void EfReadRepository_ExposesNoIQueryable()
    {
        Assert.False(ExposesIQueryable(typeof(MicroKit.Persistence.EntityFrameworkCore.EfReadRepository<,>)));
    }
}

namespace GitMcp.Core;

/// <summary>Результат построения argv для <c>git</c>: либо список аргументов (без префикса <c>git</c>), либо сообщение об ошибке валидации.</summary>
public readonly record struct GitArgsResult(IReadOnlyList<string>? Args, string? Error)
{
    public static GitArgsResult Ok(IReadOnlyList<string> args) => new(args, null);

    public static GitArgsResult Fail(string error) => new(null, error);

    public bool IsSuccess => Error is null && Args is not null;
}

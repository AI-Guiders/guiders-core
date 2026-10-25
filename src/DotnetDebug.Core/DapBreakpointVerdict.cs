namespace DotnetDebug.Core;

/// <summary>
/// Ответ адаптера на одну точку останова (DAP <c>Breakpoint</c>):
/// связалась ли она с кодом (<c>verified</c>) и, если нет, почему.
/// Без этого «точка поставлена» и «точка связалась» неразличимы.
/// </summary>
/// <param name="Line">Строка, как её вернул адаптер (0, если адаптер её не назвал).</param>
/// <param name="Verified">true — точка связана с кодом и сработает.</param>
/// <param name="Message">Причина, когда адаптер её сообщил (например, нет кода на этой строке).</param>
/// <param name="Id">Идентификатор точки у адаптера, если он его выдал.</param>
public sealed record DapBreakpointVerdict(int Line, bool Verified, string? Message, int? Id);

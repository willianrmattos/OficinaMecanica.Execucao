namespace OficinaMecanica.Execucao.Tests.Common;

// Optei por um relogio fixo em vez de DateTimeOffset.UtcNow: o "agora" do teste e exatamente o que
// o handler le do TimeProvider, entao as datas gravadas e publicadas podem ser comparadas por igualdade.
public sealed class RelogioFixo : TimeProvider
{
    private readonly DateTimeOffset _agora;

    public RelogioFixo(DateTimeOffset agora)
    {
        _agora = agora;
    }

    public override DateTimeOffset GetUtcNow() => _agora;
}

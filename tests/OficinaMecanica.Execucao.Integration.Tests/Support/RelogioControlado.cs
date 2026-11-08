namespace OficinaMecanica.Execucao.Integration.Tests.Support;

// Relogio que so anda quando o teste manda: a expiracao do cache de prontidao e verificada no instante exato, sem
// esperar o tempo passar.
public sealed class RelogioControlado : TimeProvider
{
    private DateTimeOffset _agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _agora;

    public void Avancar(TimeSpan intervalo) => _agora += intervalo;
}

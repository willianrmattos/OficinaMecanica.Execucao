using Bogus;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Tests.Common.Builders;

public class OrdemDeProducaoBuilder
{
    private static readonly Faker Faker = new("pt_BR");

    private Guid _id = Guid.NewGuid();
    private string _numero = $"OS-{Faker.Date.Past():yyyyMMdd}-{Faker.Random.AlphaNumeric(6).ToUpperInvariant()}";
    private VeiculoDaOrdem _veiculo = new VeiculoDaOrdemBuilder().Build();
    private string? _observacoes = Faker.Lorem.Sentence();
    private List<ServicoDaOrdem> _servicos = [new ServicoDaOrdemBuilder().Build()];
    private List<PecaDaOrdem> _pecas = [new PecaDaOrdemBuilder().Build()];
    private string _mecanicoId = Faker.Random.Guid().ToString();
    private DateTimeOffset _agora = Faker.Date.PastOffset();

    public OrdemDeProducaoBuilder ComId(Guid id) { _id = id; return this; }
    public OrdemDeProducaoBuilder ComNumero(string numero) { _numero = numero; return this; }
    public OrdemDeProducaoBuilder ComVeiculo(VeiculoDaOrdem veiculo) { _veiculo = veiculo; return this; }
    public OrdemDeProducaoBuilder ComObservacoes(string? observacoes) { _observacoes = observacoes; return this; }
    public OrdemDeProducaoBuilder SemObservacoes() { _observacoes = null; return this; }
    public OrdemDeProducaoBuilder ComServicos(params ServicoDaOrdem[] servicos) { _servicos = servicos.ToList(); return this; }
    public OrdemDeProducaoBuilder ComPecas(params PecaDaOrdem[] pecas) { _pecas = pecas.ToList(); return this; }
    public OrdemDeProducaoBuilder DoMecanico(string mecanicoId) { _mecanicoId = mecanicoId; return this; }
    public OrdemDeProducaoBuilder EnfileiradaEm(DateTimeOffset enfileiradaEm) { _agora = enfileiradaEm; return this; }

    public OrdemDeProducao NaFila() =>
        OrdemDeProducao.Enfileirar(_id, _numero, _veiculo, _servicos, _pecas, _observacoes, _agora);

    public OrdemDeProducao EmDiagnostico()
    {
        var ordem = NaFila();
        ordem.IniciarDiagnostico(_mecanicoId, _agora.AddMinutes(1));
        return ordem;
    }

    public OrdemDeProducao DiagnosticoConcluido(Laudo? laudo = null)
    {
        var ordem = EmDiagnostico();
        ordem.ConcluirDiagnostico(_mecanicoId, laudo ?? new LaudoBuilder().Build(), _agora.AddMinutes(2));
        return ordem;
    }

    public OrdemDeProducao AguardandoExecucao()
    {
        var ordem = DiagnosticoConcluido();
        ordem.LiberarParaExecucao(_servicos, _pecas, _agora.AddMinutes(3));
        return ordem;
    }

    public OrdemDeProducao EmExecucao(string? mecanicoDoReparo = null)
    {
        var ordem = AguardandoExecucao();
        ordem.IniciarReparo(mecanicoDoReparo ?? _mecanicoId, _agora.AddMinutes(4));
        return ordem;
    }

    public OrdemDeProducao Finalizada(string resumo = "Serviço concluído sem intercorrências.")
    {
        var ordem = EmExecucao();
        ordem.FinalizarReparo(_mecanicoId, resumo, _agora.AddMinutes(5));
        return ordem;
    }

    public OrdemDeProducao Inviavel(string motivo = "Peça indisponível no fornecedor.")
    {
        var ordem = EmExecucao();
        ordem.Inviabilizar(_mecanicoId, motivo, _agora.AddMinutes(5));
        return ordem;
    }

    public OrdemDeProducao Cancelada(string motivo = "Pagamento cancelado.")
    {
        var ordem = NaFila();
        ordem.Cancelar(motivo, _agora.AddMinutes(1));
        return ordem;
    }

    public OrdemDeProducao NoStatus(StatusProducao status) => status switch
    {
        StatusProducao.Fila => NaFila(),
        StatusProducao.EmDiagnostico => EmDiagnostico(),
        StatusProducao.DiagnosticoConcluido => DiagnosticoConcluido(),
        StatusProducao.AguardandoExecucao => AguardandoExecucao(),
        StatusProducao.EmExecucao => EmExecucao(),
        StatusProducao.Finalizada => Finalizada(),
        StatusProducao.Inviavel => Inviavel(),
        StatusProducao.Cancelada => Cancelada(),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status sem montagem no builder.")
    };
}

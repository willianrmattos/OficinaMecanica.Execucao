using OficinaMecanica.Execucao.Domain.Common;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Domain.Entities;

public class OrdemDeProducao : AggregateRoot
{
    public string Numero { get; private set; }
    public VeiculoDaOrdem Veiculo { get; private set; }
    public string? Observacoes { get; private set; }
    public StatusProducao Status { get; private set; }
    public string? MecanicoId { get; private set; }
    public Laudo? Laudo { get; private set; }
    public string? Resumo { get; private set; }
    public string? MotivoInviabilidade { get; private set; }
    public string? InviabilizadaPor { get; private set; }
    public string? MotivoCancelamento { get; private set; }
    public DateTimeOffset EnfileiradaEm { get; private set; }
    public DateTimeOffset? LiberadaEm { get; private set; }

    private List<ServicoDaOrdem> _servicos;
    public IReadOnlyCollection<ServicoDaOrdem> Servicos => _servicos.AsReadOnly();

    private List<PecaDaOrdem> _pecas;
    public IReadOnlyCollection<PecaDaOrdem> Pecas => _pecas.AsReadOnly();

    // Optei por nao marcar as duas listas abaixo como readonly: a reidratacao feita pelo repositorio
    // (Infrastructure) preenche os campos privados e o driver nao grava em campo readonly.
    private List<EtapaProducao> _etapas = new();
    public IReadOnlyCollection<EtapaProducao> Etapas => _etapas.AsReadOnly();

    private List<Apontamento> _apontamentos = new();
    public IReadOnlyCollection<Apontamento> Apontamentos => _apontamentos.AsReadOnly();

    private OrdemDeProducao(
        Guid ordemDeServicoId,
        string numero,
        VeiculoDaOrdem veiculo,
        string? observacoes,
        List<ServicoDaOrdem> servicos,
        List<PecaDaOrdem> pecas,
        DateTimeOffset agora) : base(ordemDeServicoId)
    {
        Numero = numero;
        Veiculo = veiculo;
        Observacoes = observacoes;
        _servicos = servicos;
        _pecas = pecas;
        Status = StatusProducao.Fila;
        EnfileiradaEm = agora;
    }

    // Optei por usar o proprio OrdemDeServicoId como Id: a mensagem repetida da saga cai sempre na
    // mesma ordem, o que da idempotencia ao enfileiramento sem tabela de deduplicacao. Tambem nao
    // exijo servico aqui: o diagnostico ainda vai definir o que fazer, e uma excecao de negocio num
    // consumer viraria mensagem envenenada; a regra "ao menos um servico" vale so no snapshot aprovado.
    public static OrdemDeProducao Enfileirar(
        Guid ordemDeServicoId,
        string numero,
        VeiculoDaOrdem veiculo,
        IEnumerable<ServicoDaOrdem> servicos,
        IEnumerable<PecaDaOrdem> pecas,
        string? observacoes,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(veiculo);
        ArgumentNullException.ThrowIfNull(servicos);
        ArgumentNullException.ThrowIfNull(pecas);

        if (ordemDeServicoId == Guid.Empty)
            throw new DomainException("O identificador da ordem de serviço é obrigatório.");

        if (string.IsNullOrWhiteSpace(numero))
            throw new DomainException("O número da ordem é obrigatório.");

        return new OrdemDeProducao(
            ordemDeServicoId,
            numero.Trim(),
            veiculo,
            string.IsNullOrWhiteSpace(observacoes) ? null : observacoes.Trim(),
            servicos.ToList(),
            pecas.ToList(),
            agora);
    }

    public void IniciarDiagnostico(string mecanicoId, DateTimeOffset agora)
    {
        ExigirStatus("iniciar o diagnóstico", StatusProducao.Fila);
        ExigirMecanico(mecanicoId);

        MecanicoId = mecanicoId;
        Status = StatusProducao.EmDiagnostico;
        _etapas.Add(EtapaProducao.Iniciar(NomeEtapa.Diagnostico, mecanicoId, agora));
    }

    public void RegistrarApontamento(string mecanicoId, string texto, DateTimeOffset agora)
    {
        ExigirStatus("registrar apontamento", StatusProducao.EmDiagnostico, StatusProducao.EmExecucao);
        ExigirMecanicoResponsavel(mecanicoId);

        _apontamentos.Add(Apontamento.Criar(mecanicoId, texto, agora));
    }

    public void ConcluirDiagnostico(string mecanicoId, Laudo laudo, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(laudo);
        ExigirStatus("concluir o diagnóstico", StatusProducao.EmDiagnostico);
        ExigirMecanicoResponsavel(mecanicoId);

        Laudo = laudo;
        EtapaEmAndamento(NomeEtapa.Diagnostico).Concluir(agora);
        Status = StatusProducao.DiagnosticoConcluido;
    }

    // Optei por devolver false (em vez de lancar) quando a ordem ja passou da liberacao (aguardando,
    // em execucao, finalizada ou inviavel): reentrega tardia da mesma mensagem e fluxo normal e nao
    // ha o que reaplicar. O snapshot aprovado so vale na primeira liberacao, entao uma reentrega
    // nunca o sobrescreve. Cancelada continua sendo erro: liberar o que a saga mandou cancelar
    // indica mensagens fora de ordem que nao devem passar em silencio.
    public bool LiberarParaExecucao(
        IEnumerable<ServicoDaOrdem> servicosAprovados,
        IEnumerable<PecaDaOrdem> pecasAprovadas,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(servicosAprovados);
        ArgumentNullException.ThrowIfNull(pecasAprovadas);

        if (Status is StatusProducao.AguardandoExecucao
            or StatusProducao.EmExecucao
            or StatusProducao.Finalizada
            or StatusProducao.Inviavel)
            return false;

        ExigirStatus("liberar para execução", StatusProducao.DiagnosticoConcluido);

        // Substituo servicos e pecas pelo snapshot aprovado porque o que o cliente aprovou e pagou
        // pode diferir do enfileirado: o diagnostico pode alterar os dois.
        _servicos = ExigirAoMenosUmServico(servicosAprovados);
        _pecas = pecasAprovadas.ToList();
        Status = StatusProducao.AguardandoExecucao;
        LiberadaEm = agora;
        return true;
    }

    public void IniciarReparo(string mecanicoId, DateTimeOffset agora)
    {
        ExigirStatus("iniciar o reparo", StatusProducao.AguardandoExecucao);
        ExigirMecanico(mecanicoId);

        MecanicoId = mecanicoId;
        Status = StatusProducao.EmExecucao;
        _etapas.Add(EtapaProducao.Iniciar(NomeEtapa.Reparo, mecanicoId, agora));
    }

    public void FinalizarReparo(string mecanicoId, string resumo, DateTimeOffset agora)
    {
        ExigirStatus("finalizar o reparo", StatusProducao.EmExecucao);
        ExigirMecanicoResponsavel(mecanicoId);

        if (string.IsNullOrWhiteSpace(resumo))
            throw new DomainException("O resumo da execução é obrigatório.");

        Resumo = resumo.Trim();
        EtapaEmAndamento(NomeEtapa.Reparo).Concluir(agora);
        Status = StatusProducao.Finalizada;
    }

    // Em EmExecucao so o mecanico responsavel inviabiliza. Em AguardandoExecucao ninguem assumiu o
    // reparo ainda, entao optei por aceitar qualquer mecanico identificado (a API restringe por papel)
    // e por registrar sempre quem inviabilizou, ja que nesse caso nao e o MecanicoId da ordem.
    public void Inviabilizar(string mecanicoId, string motivo, DateTimeOffset agora)
    {
        ExigirStatus("inviabilizar a execução", StatusProducao.AguardandoExecucao, StatusProducao.EmExecucao);

        if (Status == StatusProducao.EmExecucao)
            ExigirMecanicoResponsavel(mecanicoId);
        else
            ExigirMecanico(mecanicoId);

        if (string.IsNullOrWhiteSpace(motivo))
            throw new DomainException("O motivo da inviabilidade é obrigatório.");

        InterromperEtapaEmAndamento(agora);
        MotivoInviabilidade = motivo.Trim();
        InviabilizadaPor = mecanicoId;
        Status = StatusProducao.Inviavel;
    }

    // Optei por devolver false (em vez de lancar) quando a ordem ja estava cancelada: reentrega da
    // mesma mensagem e fluxo normal. Nao permito cancelar com o reparo em andamento porque a saga
    // so compensa uma execucao ja iniciada via Inviabilizar.
    public bool Cancelar(string motivo, DateTimeOffset agora)
    {
        if (Status == StatusProducao.Cancelada)
            return false;

        ExigirStatus(
            "cancelar a produção",
            StatusProducao.Fila,
            StatusProducao.EmDiagnostico,
            StatusProducao.DiagnosticoConcluido,
            StatusProducao.AguardandoExecucao);

        if (string.IsNullOrWhiteSpace(motivo))
            throw new DomainException("O motivo do cancelamento é obrigatório.");

        InterromperEtapaEmAndamento(agora);
        MotivoCancelamento = motivo.Trim();
        Status = StatusProducao.Cancelada;
        return true;
    }

    private void ExigirStatus(string acao, params StatusProducao[] permitidos)
    {
        if (!permitidos.Contains(Status))
            throw new DomainException(
                $"Não é possível {acao} com a ordem no status '{Status}' (permitido: {string.Join(", ", permitidos.Select(s => $"'{s}'"))}).");
    }

    private static void ExigirMecanico(string mecanicoId)
    {
        if (string.IsNullOrWhiteSpace(mecanicoId))
            throw new DomainException("O mecânico é obrigatório.");
    }

    // Comparo com Ordinal porque o id do mecanico e opaco (vem do token), sem normalizacao de caixa.
    private void ExigirMecanicoResponsavel(string mecanicoId)
    {
        ExigirMecanico(mecanicoId);

        if (!string.Equals(MecanicoId, mecanicoId, StringComparison.Ordinal))
            throw new DomainException("Apenas o mecânico responsável pela ordem pode executar esta ação.");
    }

    private static List<ServicoDaOrdem> ExigirAoMenosUmServico(IEnumerable<ServicoDaOrdem> servicos)
    {
        var lista = servicos.ToList();

        if (lista.Count == 0)
            throw new DomainException("A ordem deve ter pelo menos um serviço.");

        return lista;
    }

    private EtapaProducao EtapaEmAndamento(NomeEtapa nome) =>
        _etapas.Single(e => e.Nome == nome && e.Status == StatusEtapa.EmAndamento);

    private void InterromperEtapaEmAndamento(DateTimeOffset agora) =>
        _etapas.FirstOrDefault(e => e.Status == StatusEtapa.EmAndamento)?.Interromper(agora);
}

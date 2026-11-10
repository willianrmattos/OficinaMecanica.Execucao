using System.Net;
using System.Net.Http.Json;
using OficinaMecanica.Contracts.Events;
using OficinaMecanica.Execucao.API.Middleware;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Domain.Enums;

namespace OficinaMecanica.Execucao.Integration.Tests.Api;

public class ControllersTests : ApiTestBase
{
    private const string Mecanico = "mecanico-do-token";

    private HttpClient Cliente(string sub = Mecanico) => Api.CriarClienteAutenticado(sub);

    // ---- fila de diagnostico ----

    [Fact]
    public async Task ReivindicarDiagnostico_ComOrdemNaFila_DeveRetornar200UsandoOMecanicoDoToken()
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();
        Api.Ordens.Semear(ordem);

        // Um "mecanicoId" no corpo, ainda que enviado, nunca e considerado: o dono e sempre o do token.
        var resposta = await Cliente().PostAsJsonAsync("/api/diagnosticos/proximo", new { mecanicoId = "impostor" });

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resposta.Content.ReadFromJsonAsync<OrdemDeProducaoDto>();
        dto!.Id.Should().Be(ordem.Id);
        dto.Status.Should().Be("EmDiagnostico");
        dto.MecanicoId.Should().Be(Mecanico);
        Api.Publicador.De<DiagnosticoIniciado>().Should().ContainSingle().Which.MecanicoId.Should().Be(Mecanico);
        Api.UnitOfWork.Salvamentos.Should().Be(1);
    }

    [Fact]
    public async Task ReivindicarDiagnostico_ComFilaVazia_DeveRetornar204SemPublicarNada()
    {
        var resposta = await Cliente().PostAsync("/api/diagnosticos/proximo", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Api.Publicador.Publicadas.Should().BeEmpty();
    }

    [Fact]
    public async Task ReivindicarDiagnostico_DeveSeguirAOrdemDaFilaEntreChamadas()
    {
        var antiga = new OrdemDeProducaoBuilder().EnfileiradaEm(DateTimeOffset.UtcNow.AddHours(-3)).NaFila();
        var nova = new OrdemDeProducaoBuilder().EnfileiradaEm(DateTimeOffset.UtcNow.AddHours(-1)).NaFila();
        Api.Ordens.Semear(nova);
        Api.Ordens.Semear(antiga);

        var primeira = await (await Cliente("m1").PostAsync("/api/diagnosticos/proximo", null)).Content.ReadFromJsonAsync<OrdemDeProducaoDto>();
        var segunda = await (await Cliente("m2").PostAsync("/api/diagnosticos/proximo", null)).Content.ReadFromJsonAsync<OrdemDeProducaoDto>();

        primeira!.Id.Should().Be(antiga.Id);
        segunda!.Id.Should().Be(nova.Id);
    }

    // ---- fila de reparo ----

    [Fact]
    public async Task ReivindicarReparo_ComOrdemAguardandoExecucao_DeveRetornar200ComOMecanicoDoToken()
    {
        var ordem = new OrdemDeProducaoBuilder().AguardandoExecucao();
        Api.Ordens.Semear(ordem);

        var resposta = await Cliente().PostAsync("/api/reparos/proximo", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resposta.Content.ReadFromJsonAsync<OrdemDeProducaoDto>();
        dto!.Status.Should().Be("EmExecucao");
        dto.MecanicoId.Should().Be(Mecanico);
        Api.Publicador.Publicadas.Should().BeEmpty("o inicio do reparo nao e comunicado a saga");
    }

    [Fact]
    public async Task ReivindicarReparo_ComFilaVazia_DeveRetornar204()
    {
        (await Cliente().PostAsync("/api/reparos/proximo", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---- apontamento ----

    [Fact]
    public async Task RegistrarApontamento_DoMecanicoResponsavel_DeveRetornar200()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmDiagnostico();
        Api.Ordens.Semear(ordem);

        var resposta = await Cliente().PostAsJsonAsync($"/api/ordens-de-producao/{ordem.Id}/apontamentos", new { texto = "Disco empenado." });

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resposta.Content.ReadFromJsonAsync<OrdemDeProducaoDto>();
        dto!.Apontamentos.Should().ContainSingle().Which.Should().Match<ApontamentoDto>(a => a.MecanicoId == Mecanico && a.Texto == "Disco empenado.");
    }

    [Fact]
    public async Task RegistrarApontamento_DeOutroMecanico_DeveRetornar400ComMensagemDeNegocio()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("dono-da-ordem").EmDiagnostico();
        Api.Ordens.Semear(ordem);

        var resposta = await Cliente("intruso").PostAsJsonAsync($"/api/ordens-de-producao/{ordem.Id}/apontamentos", new { texto = "x" });

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var erro = await resposta.Content.ReadFromJsonAsync<ErrorResponse>();
        erro!.Tipo.Should().Be("Erro de Negócio");
        erro.Mensagem.Should().Contain("mecânico responsável");
    }

    // ---- diagnostico ----

    [Fact]
    public async Task ConcluirDiagnostico_DeveRetornar200PublicarOLaudoEPersistir()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmDiagnostico();
        Api.Ordens.Semear(ordem);

        var resposta = await Cliente().PostAsJsonAsync(
            $"/api/ordens-de-producao/{ordem.Id}/diagnostico/concluir", new { laudo = "  Pastilhas gastas.  ", horasEstimadas = 3.5 });

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resposta.Content.ReadFromJsonAsync<OrdemDeProducaoDto>();
        dto!.Status.Should().Be("DiagnosticoConcluido");
        dto.Laudo!.HorasEstimadas.Should().Be(3.5m);
        var evento = Api.Publicador.De<DiagnosticoConcluido>().Should().ContainSingle().Subject;
        evento.Laudo.Should().Be("Pastilhas gastas.");
        evento.MecanicoId.Should().Be(Mecanico);
        evento.OrdemDeServicoId.Should().Be(ordem.Id);
        evento.MessageId.Should().NotBeEmpty();
        Api.UnitOfWork.Salvamentos.Should().Be(1);
    }

    [Fact]
    public async Task ConcluirDiagnostico_ComLaudoVazio_DeveRetornar400ListandoOCampo()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmDiagnostico();
        Api.Ordens.Semear(ordem);

        var resposta = await Cliente().PostAsJsonAsync($"/api/ordens-de-producao/{ordem.Id}/diagnostico/concluir", new { laudo = "" });

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var erro = await resposta.Content.ReadFromJsonAsync<ErrorResponse>();
        erro!.Tipo.Should().Be("Erro de Validação");
        erro.Erros.Should().Contain(e => e.Campo == "Laudo");
        Api.Publicador.Publicadas.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcluirDiagnostico_SemCampoLaudoNoCorpo_DeveRetornar400NoFormatoDeErroDaApi()
    {
        var resposta = await Cliente().PostAsJsonAsync($"/api/ordens-de-producao/{Guid.NewGuid()}/diagnostico/concluir", new { });

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadFromJsonAsync<ErrorResponse>())!.Tipo.Should().Be("Erro de Validação");
    }

    [Fact]
    public async Task ConcluirDiagnostico_DeOrdemInexistente_DeveRetornar404()
    {
        var resposta = await Cliente().PostAsJsonAsync($"/api/ordens-de-producao/{Guid.NewGuid()}/diagnostico/concluir", new { laudo = "ok" });

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await resposta.Content.ReadFromJsonAsync<ErrorResponse>())!.Mensagem.Should().Be("Ordem de produção não encontrada.");
    }

    [Fact]
    public async Task ConcluirDiagnostico_DeOutroMecanico_DeveRetornar400()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("dono").EmDiagnostico();
        Api.Ordens.Semear(ordem);

        var resposta = await Cliente("outro").PostAsJsonAsync($"/api/ordens-de-producao/{ordem.Id}/diagnostico/concluir", new { laudo = "ok" });

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Api.Publicador.Publicadas.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcluirDiagnostico_ComIdQueNaoEGuid_DeveRetornar404PorRota()
    {
        var resposta = await Cliente().PostAsJsonAsync("/api/ordens-de-producao/nao-e-guid/diagnostico/concluir", new { laudo = "ok" });

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- reparo ----

    [Fact]
    public async Task FinalizarReparo_DeveRetornar200EPublicarExecucaoFinalizada()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmExecucao();
        Api.Ordens.Semear(ordem);

        var resposta = await Cliente().PostAsJsonAsync($"/api/ordens-de-producao/{ordem.Id}/reparo/finalizar", new { resumo = "Troca concluida." });

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadFromJsonAsync<OrdemDeProducaoDto>())!.Status.Should().Be("Finalizada");
        Api.Publicador.De<ExecucaoFinalizada>().Should().ContainSingle().Which.Resumo.Should().Be("Troca concluida.");
    }

    [Fact]
    public async Task FinalizarReparo_ComOrdemNaoEmExecucao_DeveRetornar400()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmDiagnostico();
        Api.Ordens.Semear(ordem);

        var resposta = await Cliente().PostAsJsonAsync($"/api/ordens-de-producao/{ordem.Id}/reparo/finalizar", new { resumo = "x" });

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InviabilizarReparo_DeveRetornar200EPublicarExecucaoInviavel()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmExecucao();
        Api.Ordens.Semear(ordem);

        var resposta = await Cliente().PostAsJsonAsync($"/api/ordens-de-producao/{ordem.Id}/reparo/inviabilizar", new { motivo = "Peca descontinuada." });

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadFromJsonAsync<OrdemDeProducaoDto>())!.InviabilizadaPor.Should().Be(Mecanico);
        Api.Publicador.De<ExecucaoInviavel>().Should().ContainSingle().Which.Motivo.Should().Be("Peca descontinuada.");
    }

    [Fact]
    public async Task InviabilizarReparo_EmAguardandoExecucao_DeveAceitarQualquerMecanicoIdentificado()
    {
        var ordem = new OrdemDeProducaoBuilder().AguardandoExecucao();
        Api.Ordens.Semear(ordem);

        var resposta = await Cliente("supervisor").PostAsJsonAsync($"/api/ordens-de-producao/{ordem.Id}/reparo/inviabilizar", new { motivo = "Cliente nao autorizou." });

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadFromJsonAsync<OrdemDeProducaoDto>())!.InviabilizadaPor.Should().Be("supervisor");
    }

    // ---- consulta ----

    [Fact]
    public async Task ObterOrdem_Existente_DeveRetornar200ComODetalhe()
    {
        var ordem = new OrdemDeProducaoBuilder().Finalizada();
        Api.Ordens.Semear(ordem);

        var resposta = await Cliente().GetAsync($"/api/ordens-de-producao/{ordem.Id}");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resposta.Content.ReadFromJsonAsync<OrdemDeProducaoDto>();
        dto!.Id.Should().Be(ordem.Id);
        dto.Numero.Should().Be(ordem.Numero);
        dto.Etapas.Should().HaveCount(2);
        dto.Resumo.Should().Be(ordem.Resumo);
    }

    [Fact]
    public async Task ObterOrdem_Inexistente_DeveRetornar404()
    {
        var resposta = await Cliente().GetAsync($"/api/ordens-de-producao/{Guid.NewGuid()}");

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await resposta.Content.ReadFromJsonAsync<ErrorResponse>())!.Tipo.Should().Be("Não Encontrado");
    }

    [Fact]
    public async Task ListarFila_DeveFiltrarPorStatusMecanicoEPaginar()
    {
        var fila = Enumerable.Range(0, 3).Select(i => new OrdemDeProducaoBuilder().EnfileiradaEm(DateTimeOffset.UtcNow.AddMinutes(-i)).NaFila()).ToList();
        foreach (var ordem in fila)
            Api.Ordens.Semear(ordem);
        var emDiagnostico = new OrdemDeProducaoBuilder().DoMecanico("m-lista").EmDiagnostico();
        Api.Ordens.Semear(emDiagnostico);

        var paginaUm = await Cliente().GetFromJsonAsync<PaginacaoResultDto<OrdemDeProducaoResumoDto>>("/api/fila?status=Fila&pagina=1&tamanho=2");
        var doMecanico = await Cliente().GetFromJsonAsync<PaginacaoResultDto<OrdemDeProducaoResumoDto>>("/api/fila?mecanicoId=m-lista");

        paginaUm!.Total.Should().Be(3);
        paginaUm.TotalPaginas.Should().Be(2);
        paginaUm.Itens.Select(i => i.Id).Should().Equal(fila.OrderBy(o => o.EnfileiradaEm).Take(2).Select(o => o.Id));
        doMecanico!.Itens.Should().ContainSingle().Which.Id.Should().Be(emDiagnostico.Id);
    }

    [Theory]
    [InlineData("/api/fila?pagina=0")]
    [InlineData("/api/fila?tamanho=101")]
    [InlineData("/api/fila?status=NaoExiste")]
    public async Task ListarFila_ComParametrosInvalidos_DeveRetornar400(string rota)
    {
        var resposta = await Cliente().GetAsync(rota);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadFromJsonAsync<ErrorResponse>())!.Tipo.Should().Be("Erro de Validação");
    }

    [Fact]
    public async Task Resumo_DeveListarTodosOsStatusMesmoComZero()
    {
        Api.Ordens.Semear(new OrdemDeProducaoBuilder().NaFila());
        Api.Ordens.Semear(new OrdemDeProducaoBuilder().NaFila());
        Api.Ordens.Semear(new OrdemDeProducaoBuilder().Finalizada());

        var resumo = await Cliente().GetFromJsonAsync<ResumoDaFilaDto>("/api/fila/resumo");

        resumo!.Total.Should().Be(3);
        resumo.PorStatus.Keys.Should().BeEquivalentTo(Enum.GetNames<StatusProducao>());
        resumo.PorStatus["Fila"].Should().Be(2);
        resumo.PorStatus["Cancelada"].Should().Be(0);
    }

    [Fact]
    public async Task CorpoComJsonMalformado_DeveRetornar400NoFormatoDeErroDaApi()
    {
        var conteudo = new StringContent("{ isto nao e json", System.Text.Encoding.UTF8, "application/json");

        var resposta = await Cliente().PostAsync($"/api/ordens-de-producao/{Guid.NewGuid()}/diagnostico/concluir", conteudo);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadFromJsonAsync<ErrorResponse>())!.Erros.Should().NotBeNullOrEmpty();
    }
}

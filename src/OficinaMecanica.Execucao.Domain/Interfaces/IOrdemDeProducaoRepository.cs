using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;

namespace OficinaMecanica.Execucao.Domain.Interfaces;

public interface IOrdemDeProducaoRepository
{
    Task<OrdemDeProducao?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default);

    // Optei por devolver false, sem alterar nada, quando ja existe ordem com o mesmo Id: a reentrega
    // da mesma mensagem e fluxo normal, nao erro. So da para devolver false fora de transacao ou com
    // upsert ($setOnInsert); dentro de uma transacao o E11000 aborta tudo e a excecao sobe, e o retry
    // de TransientTransactionError reexecuta o handler inteiro (cai na pre-checagem de existencia).
    // A corrida com ICancelamentoPrevioRepository exige a mesma chave unica: ver o comentario dele.
    Task<bool> AdicionarAsync(OrdemDeProducao ordem, CancellationToken cancellationToken = default);

    // Concorrencia otimista: so grava se a Version persistida for a mesma com que a ordem foi lida;
    // caso contrario lanca ConcorrenciaException. Quem implementa incrementa a Version da ordem.
    // Optei por exigir que a gravacao aconteca aqui, na hora, e nao adiada para o commit: a
    // ConcorrenciaException tem que sair deste metodo e nunca de IUnitOfWork.SaveChangesAsync, porque a
    // reivindicacao da fila so retenta o que sai daqui. Depois da excecao o UoW (sessao) precisa
    // continuar utilizavel, ja que a nova tentativa le e grava de novo na mesma unidade de trabalho.
    Task AtualizarAsync(OrdemDeProducao ordem, CancellationToken cancellationToken = default);

    // FIFO pelo momento em que a ordem entrou NA FILA CONSULTADA: EnfileiradaEm na fila de diagnostico
    // (Fila) e LiberadaEm na de reparo (AguardandoExecucao), porque o que decide a vez do reparo e
    // quando a ordem ficou pronta, nao quando chegou. LiberadaEm esta sempre preenchida em
    // AguardandoExecucao (so se chega la por LiberarParaExecucao).
    // Optei por nao reservar a ordem aqui: a reserva acontece em AtualizarAsync, que falha para quem
    // perdeu a corrida. Para o retry da reivindicacao funcionar, a releitura depois de uma
    // ConcorrenciaException precisa enxergar o estado ja commitado (nova transacao/snapshot), senao
    // devolveria a mesma ordem disputada. E o claim (AtualizarAsync) precisa estar na MESMA transacao
    // do outbox, senao a ordem pode ficar EmDiagnostico sem o DiagnosticoIniciado ter sido publicado.
    Task<OrdemDeProducao?> ObterProximaDaFilaAsync(StatusProducao status, CancellationToken cancellationToken = default);

    // Com filtro de status segue a mesma ordenacao de ObterProximaDaFilaAsync (EnfileiradaEm para Fila,
    // LiberadaEm para AguardandoExecucao), para a listagem refletir a ordem de consumo; para os demais
    // status e sem filtro, EnfileiradaEm crescente.
    Task<IReadOnlyList<OrdemDeProducao>> ListarAsync(
        StatusProducao? status, string? mecanicoId, int pagina, int tamanho, CancellationToken cancellationToken = default);

    Task<int> ContarAsync(StatusProducao? status, string? mecanicoId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<StatusProducao, int>> ContarPorStatusAsync(CancellationToken cancellationToken = default);
}

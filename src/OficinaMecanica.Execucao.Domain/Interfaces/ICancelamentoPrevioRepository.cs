using OficinaMecanica.Execucao.Domain.Entities;

namespace OficinaMecanica.Execucao.Domain.Interfaces;

// Optei por modelar ordem e tombstone como agregados distintos, e isso deixa um requisito para quem
// implementa: uma transacao Mongo (snapshot) NAO impede write skew entre os dois. A saga manda
// SolicitarDiagnostico e depois CancelarProducao ao mesmo endpoint, entao as duas mensagens podem
// rodar em paralelo: T1 insere a ordem, T2 insere o tombstone (nenhuma enxerga a escrita da outra),
// as duas commitam e sobra uma ordem orfa na Fila. A implementacao DEVE fazer as duas escritas
// colidirem na MESMA chave unica (mesma colecao com _id = OrdemDeServicoId, tombstone discriminado e
// ignorado por IOrdemDeProducaoRepository.ObterPorIdAsync e pelas filas) OU serializar por
// OrdemDeServicoId. O conflito aborta a transacao do perdedor, o retry da mensagem reexecuta o handler
// inteiro e a pre-checagem de ordem/tombstone ja existente vira no-op.
public interface ICancelamentoPrevioRepository
{
    Task<CancelamentoPrevio?> ObterAsync(Guid ordemDeServicoId, CancellationToken cancellationToken = default);

    // Optei por devolver false, sem alterar nada, quando o tombstone ja existe: duas entregas do
    // mesmo cancelamento sao fluxo normal, nao erro. So da para devolver false fora de transacao ou
    // com upsert ($setOnInsert); dentro de uma transacao o E11000 aborta tudo e a excecao sobe, e o
    // retry de TransientTransactionError reexecuta o handler inteiro (cai na pre-checagem).
    Task<bool> AdicionarAsync(CancelamentoPrevio cancelamento, CancellationToken cancellationToken = default);
}

using OficinaMecanica.Contracts.Common;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Application.Common;

internal static class ItensDaMensagemExtensions
{
    public static List<ServicoDaOrdem> ParaServicosDaOrdem(this IEnumerable<ItemDeServicoMensagem> itens) =>
        itens.Select(i => ServicoDaOrdem.Criar(i.ServicoId, i.Nome, i.Quantidade, i.PrecoUnitario)).ToList();

    public static List<PecaDaOrdem> ParaPecasDaOrdem(this IEnumerable<ItemDePecaMensagem> itens) =>
        itens.Select(i => PecaDaOrdem.Criar(i.PecaId, i.Nome, i.Quantidade, i.PrecoUnitario)).ToList();
}

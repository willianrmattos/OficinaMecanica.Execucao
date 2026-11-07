using System.Security.Claims;

namespace OficinaMecanica.Execucao.API.Extensions;

public static class UsuarioAutenticadoExtensions
{
    // O mecanico e sempre quem esta no token (claim "sub", como o Seguranca emite), nunca um campo do corpo:
    // assim ninguem apropria uma ordem em nome de outro. O fallback cobre o nome mapeado pelo handler legado.
    public static string ObterIdDoMecanico(this ClaimsPrincipal usuario)
    {
        var id = usuario.FindFirst("sub")?.Value ?? usuario.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(id))
            throw new UnauthorizedAccessException("O token não identifica o mecânico (claim 'sub' ausente).");

        return id;
    }
}

namespace OficinaMecanica.Execucao.Domain.Exceptions;

// Optei por NAO herdar de DomainException: conflito de versao nao e violacao de regra de negocio
// (deve virar 409, nao 400, para quem chama) e e o unico erro que a reivindicacao da fila retenta.
public class ConcorrenciaException : Exception
{
    public ConcorrenciaException(string mensagem) : base(mensagem) { }
}

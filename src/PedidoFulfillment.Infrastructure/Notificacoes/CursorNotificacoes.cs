namespace PedidoFulfillment.Infrastructure.Notificacoes;

/// <summary>Cursor da distribuição dos avisos em entregas (REQ-18, R-05 revista): a <c>Posicao</c> do último aviso distribuído.</summary>
internal sealed class CursorNotificacoes
{
    public const int IdUnico = 1;

    public int Id { get; set; }

    public byte[] Posicao { get; set; } = new byte[8];
}

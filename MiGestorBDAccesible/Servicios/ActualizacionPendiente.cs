namespace MiGestorBDAccesible.Servicios
{
    public static class ActualizacionPendiente
    {
        public static string? Instalador { get; set; }

        public static event Action? CierreCancelado;

        public static void AvisarCierreCancelado()
        {
            Instalador = null;
            CierreCancelado?.Invoke();
        }
    }
}

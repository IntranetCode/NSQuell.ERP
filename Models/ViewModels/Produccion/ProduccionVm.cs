namespace ERP.NSQuell.Models.ViewModels.Produccion
{
    // PRODUCCION_MVP_DIA1
    public sealed class ProduccionIndexVm
    {
        public string? Busqueda { get; set; }

        public int? EstatusID { get; set; }

        public int TotalProgramas { get; set; }

        public int TotalProgramado { get; set; }

        public int TotalProducido { get; set; }

        public int TotalPendiente { get; set; }

        public int ConProgramacion { get; set; }

        public int Incompletos { get; set; }

        public List<ProduccionProgramaVm>
            Programas { get; set; } = new();
    }

    public sealed class ProduccionProgramaVm
    {
        public int ProgramaProduccionID { get; set; }

        public int? SolicitudProduccionID { get; set; }

        public int? SolicitudProduccionDetalleID { get; set; }

        public string NumeroOF { get; set; } =
            string.Empty;

        public string FolioSolicitud { get; set; } =
            string.Empty;

        public string NumeroOFRecibida { get; set; } =
            string.Empty;
        public string Cliente { get; set; } =
            string.Empty;

        public string NumeroParte { get; set; } =
            string.Empty;

        public string ReferenciaSAP { get; set; } =
            string.Empty;

        public string Designacion { get; set; } =
            string.Empty;

        public int CantidadRequerida { get; set; }

        public int PiezasDesdePT { get; set; }

        public int CantidadProgramada { get; set; }

        public int CantidadProducida { get; set; }

        public int CantidadPendiente { get; set; }

        public int? MaquinaID { get; set; }

        public string MaquinaCodigo { get; set; } =
            string.Empty;

        public string MaquinaNombre { get; set; } =
            string.Empty;

        public int? MoldeID { get; set; }

        public string MoldeCodigo { get; set; } =
            string.Empty;

        public string CondicionProduccion { get; set; } =
            string.Empty;

        public int? SecuenciaMaquina { get; set; }

        public DateTime? FechaInicioProgramada { get; set; }

        public DateTime? FechaFinProgramada { get; set; }

        public decimal? HorasProgramadas { get; set; }

        public DateTime? FechaInicioReal { get; set; }

        public DateTime? FechaFinReal { get; set; }

        public decimal? HorasReales { get; set; }

        public int? ObjetivoHora { get; set; }

        public string Ciclo { get; set; } =
            string.Empty;

        public int? Cavidades { get; set; }

        public string MaterialCodigo { get; set; } =
            string.Empty;

        public string MaterialDescripcion { get; set; } =
            string.Empty;

        public decimal? CantidadMpKg { get; set; }

        public string EmbalajeCodigo { get; set; } =
            string.Empty;

        public string EmbalajeDescripcion { get; set; } =
            string.Empty;

        public decimal? CantidadEmbalajes { get; set; }

        public int EstatusID { get; set; }

        public string Observaciones { get; set; } =
            string.Empty;

        public DateTime? FechaGeneracionOF { get; set; }

        public decimal AvancePorcentaje =>
            CantidadProgramada <= 0
                ? 0
                : Math.Min(
                    100,
                    Math.Round(
                        CantidadProducida
                        * 100m
                        / CantidadProgramada,
                        1));

        public bool ProgramacionCompleta =>
            SolicitudProduccionID.HasValue
            && MaquinaID.HasValue
            && CantidadProgramada > 0
            && FechaInicioProgramada.HasValue;

        public string EstatusNombre =>
            global::ERP.NSQuell.Models.PlaneacionOFEstatus
                .Nombre(EstatusID);

        public string EstatusClase =>
            EstatusID switch
            {
                8 => "prod-status-ready",
                9 => "prod-status-running",
                10 => "prod-status-closed",
                11 => "prod-status-cancelled",
                _ => "prod-status-pending"
            };
    }
}



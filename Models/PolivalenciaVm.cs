namespace ERP.NSQuell.Models;

public sealed class PolivalenciaIndexVm
{
    public bool Configurado { get; set; }
    public int? ParteID { get; set; }
    public string? Busqueda { get; set; }
    public string FuenteDocumento { get; set; } = string.Empty;
    public string VersionDocumento { get; set; } = string.Empty;
    public string Periodo { get; set; } = string.Empty;
    public int TotalOperadores { get; set; }
    public int TotalPartesMapeadas { get; set; }
    public int TotalPartesSinMapeo { get; set; }
    public List<PolivalenciaParteOpcionVm> Partes { get; set; } = new();
    public List<PolivalenciaOperadorResumenVm> Operadores { get; set; } = new();
    public List<PolivalenciaCompetenciaVm> Competencias { get; set; } = new();
    public List<PolivalenciaSinMapeoVm> SinMapeo { get; set; } = new();
}

public sealed class PolivalenciaParteOpcionVm
{
    public int ParteID { get; set; }
    public string NumeroParte { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Texto => string.IsNullOrWhiteSpace(Descripcion)
        ? NumeroParte
        : $"{NumeroParte} - {Descripcion}";
}

public sealed class PolivalenciaOperadorResumenVm
{
    public int PersonalID { get; set; }
    public string NumeroControl { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Puesto { get; set; } = string.Empty;
    public int NivelMaximo { get; set; }
    public int PartesEvaluadas { get; set; }
    public int Nivel4 { get; set; }
    public int Nivel3 { get; set; }
    public int Nivel2 { get; set; }
    public int Nivel1 { get; set; }
    public int PartesDominioAlto => Nivel4 + Nivel3;
}

public sealed class PolivalenciaCompetenciaVm
{
    public int PersonalID { get; set; }
    public int ParteID { get; set; }
    public string NumeroControl { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Puesto { get; set; } = string.Empty;
    public string NumeroParte { get; set; } = string.Empty;
    public string ReferenciaSAP { get; set; } = string.Empty;
    public string Designacion { get; set; } = string.Empty;
    public string DescripcionParte { get; set; } = string.Empty;
    public int Nivel { get; set; }
    public bool EnEscalaActual { get; set; }
}

public sealed class PolivalenciaSinMapeoVm
{
    public string ClaveMatriz { get; set; } = string.Empty;
    public string EncabezadoMatriz { get; set; } = string.Empty;
}

public sealed class PolivalenciaOperadorDetalleVm
{
    public bool Configurado { get; set; }
    public int PersonalID { get; set; }
    public string NumeroControl { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Puesto { get; set; } = string.Empty;
    public int NivelMaximo { get; set; }
    public int PartesEvaluadas { get; set; }
    public int Nivel4 { get; set; }
    public int Nivel3 { get; set; }
    public int Nivel2 { get; set; }
    public int Nivel1 { get; set; }
    public int? NivelFiltro { get; set; }
    public string? Busqueda { get; set; }
    public bool EnEscalaActual { get; set; }
    public string EscalaFolio { get; set; } = string.Empty;
    public string FuncionActual { get; set; } = string.Empty;
    public string MaquinaActual { get; set; } = string.Empty;
    public string TurnoActual { get; set; } = string.Empty;
    public List<PolivalenciaParteDetalleVm> Partes { get; set; } = new();
}

public sealed class PolivalenciaParteDetalleVm
{
    public int ParteID { get; set; }
    public string NumeroParte { get; set; } = string.Empty;
    public string ReferenciaSAP { get; set; } = string.Empty;
    public string Designacion { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string ClienteCodigo { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public int Nivel { get; set; }
    public string TipoProceso { get; set; } = string.Empty;
    public string MaquinaPrincipal { get; set; } = string.Empty;
    public string MaquinaSustituta { get; set; } = string.Empty;
    public string Ciclo { get; set; } = string.Empty;
    public int? ObjetivoHora { get; set; }
    public string MaterialCodigo { get; set; } = string.Empty;
    public string MaterialDescripcion { get; set; } = string.Empty;
}

using MiGestorBDAccesible.Core.Definicion;
using System.Text;

namespace MiGestorBDAccesible.Core.Proveedores.SqlServer
{
    public partial class ProveedorSqlServer
    {
        private const string CategoriaEnteros = "Números enteros";
        private const string CategoriaDecimales = "Números con decimales";
        private const string CategoriaTexto = "Texto";
        private const string CategoriaFechas = "Fecha y hora";
        private const string CategoriaOtros = "Otros";

        private static readonly IReadOnlyList<TipoDatosModelo> _tiposDatos =
        [
            new() { Nombre = "int", Categoria = CategoriaEnteros, AdmiteIdentidad = true, Descripcion = "número entero hasta unos 2 mil millones; el más usado para Id" },
            new() { Nombre = "bigint", Categoria = CategoriaEnteros, AdmiteIdentidad = true, Descripcion = "número entero muy grande" },
            new() { Nombre = "smallint", Categoria = CategoriaEnteros, AdmiteIdentidad = true, Descripcion = "número entero hasta 32 767" },
            new() { Nombre = "tinyint", Categoria = CategoriaEnteros, AdmiteIdentidad = true, Descripcion = "número entero de 0 a 255" },
            new() { Nombre = "decimal", Categoria = CategoriaDecimales, UsaPrecision = true, Descripcion = "número con decimales exactos; ideal para dinero" },
            new() { Nombre = "money", Categoria = CategoriaDecimales, Descripcion = "dinero con 4 decimales fijos" },
            new() { Nombre = "float", Categoria = CategoriaDecimales, Descripcion = "número aproximado, para cálculos científicos" },
            new() { Nombre = "nvarchar", Categoria = CategoriaTexto, UsaLongitud = true, AdmiteMax = true, LongitudPredeterminada = 100, LongitudMaxima = 4000, Descripcion = "texto con acentos y cualquier idioma, de largo variable" },
            new() { Nombre = "nchar", Categoria = CategoriaTexto, UsaLongitud = true, LongitudPredeterminada = 10, LongitudMaxima = 4000, Descripcion = "texto Unicode de largo fijo" },
            new() { Nombre = "varchar", Categoria = CategoriaTexto, UsaLongitud = true, AdmiteMax = true, LongitudPredeterminada = 100, LongitudMaxima = 8000, Descripcion = "texto sin Unicode, de largo variable" },
            new() { Nombre = "char", Categoria = CategoriaTexto, UsaLongitud = true, LongitudPredeterminada = 10, LongitudMaxima = 8000, Descripcion = "texto de largo fijo, como códigos" },
            new() { Nombre = "date", Categoria = CategoriaFechas, Descripcion = "solo la fecha" },
            new() { Nombre = "datetime2", Categoria = CategoriaFechas, UsaFraccionSegundos = true, Descripcion = "fecha y hora; el recomendado" },
            new() { Nombre = "time", Categoria = CategoriaFechas, UsaFraccionSegundos = true, Descripcion = "solo la hora" },
            new() { Nombre = "datetimeoffset", Categoria = CategoriaFechas, UsaFraccionSegundos = true, Descripcion = "fecha y hora con zona horaria" },
            new() { Nombre = "datetime", Categoria = CategoriaFechas, Descripcion = "fecha y hora, tipo antiguo" },
            new() { Nombre = "bit", Categoria = CategoriaOtros, Descripcion = "sí o no: 1 o 0" },
            new() { Nombre = "uniqueidentifier", Categoria = CategoriaOtros, Descripcion = "identificador global único (GUID)" },
            new() { Nombre = "varbinary", Categoria = CategoriaOtros, UsaLongitud = true, AdmiteMax = true, LongitudPredeterminada = 100, LongitudMaxima = 8000, Descripcion = "datos binarios, como archivos" }
        ];

        public IReadOnlyList<TipoDatosModelo> TiposDatos => _tiposDatos;

        public string ScriptCrearTabla(DefinicionTablaModelo tabla)
        {
            var nombreTabla = string.IsNullOrWhiteSpace(tabla.Nombre) ? "NuevaTabla" : tabla.Nombre.Trim();
            var esquema = string.IsNullOrWhiteSpace(tabla.Esquema) ? "dbo" : tabla.Esquema.Trim();
            var lineas = new List<string>();

            foreach (var columna in tabla.Columnas)
            {
                var nombre = string.IsNullOrWhiteSpace(columna.Nombre) ? "SinNombre" : columna.Nombre.Trim();
                var linea = new StringBuilder($"{Delimitar(nombre)} {columna.TipoCompleto}");

                if (columna.EsIdentidad)
                {
                    linea.Append($" IDENTITY({columna.Semilla}, {columna.Incremento})");
                }

                linea.Append(columna.PermiteNulos ? " NULL" : " NOT NULL");

                if (!string.IsNullOrWhiteSpace(columna.ValorPredeterminado))
                {
                    linea.Append($" CONSTRAINT {Delimitar($"DF_{nombreTabla}_{nombre}")} DEFAULT ({columna.ValorPredeterminado.Trim()})");
                }

                lineas.Add(linea.ToString());
            }

            var clave = tabla.Columnas.Where(c => c.EsClavePrincipal).Select(c => Delimitar(c.Nombre.Trim())).ToList();

            if (clave.Count > 0)
            {
                lineas.Add($"CONSTRAINT {Delimitar($"PK_{nombreTabla}")} PRIMARY KEY ({string.Join(", ", clave)})");
            }

            foreach (var unica in tabla.Columnas.Where(c => c.EsUnica && !(c.EsClavePrincipal && clave.Count == 1)))
            {
                lineas.Add($"CONSTRAINT {Delimitar($"UQ_{nombreTabla}_{unica.Nombre.Trim()}")} UNIQUE ({Delimitar(unica.Nombre.Trim())})");
            }

            foreach (var columna in tabla.Columnas.Where(c => c.Referencia is not null && !string.IsNullOrWhiteSpace(c.Referencia.ColumnaDestino)))
            {
                lineas.Add(RestriccionLlave(nombreTabla, columna.Nombre.Trim(), columna.Referencia!));
            }

            var script = new StringBuilder();
            script.AppendLine($"CREATE TABLE {Delimitar(esquema)}.{Delimitar(nombreTabla)} (");

            if (lineas.Count > 0)
            {
                script.AppendLine(string.Join($",{Environment.NewLine}", lineas.Select(l => $"    {l}")));
            }

            script.Append(");");
            return script.ToString();
        }
    }
}

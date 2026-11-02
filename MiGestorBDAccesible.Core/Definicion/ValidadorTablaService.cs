using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores;

namespace MiGestorBDAccesible.Core.Definicion
{
    public record ProblemaValidacion(string Mensaje, bool EsAdvertencia = false);

    public class ValidadorTablaService
    {
        public List<ProblemaValidacion> ValidarColumna(
            DefinicionColumnaModelo columna,
            IEnumerable<DefinicionColumnaModelo> otras,
            IReadOnlyList<TipoDatosModelo> tipos,
            TerminosMotorModelo? terminos = null)
        {
            terminos ??= CapacidadesMotorModelo.SqlServer.Terminos;
            var problemas = new List<ProblemaValidacion>();
            var nombre = string.IsNullOrWhiteSpace(columna.Nombre) ? "La columna" : columna.Nombre.Trim();

            if (string.IsNullOrWhiteSpace(columna.Nombre))
            {
                problemas.Add(new("Escribe el nombre de la columna."));
            }
            else if (otras.Any(o => o.Nombre.Trim().Equals(columna.Nombre.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                problemas.Add(new($"Ya hay una columna llamada {nombre}. Cada columna necesita un nombre distinto."));
            }

            var tipo = tipos.FirstOrDefault(t => t.Nombre == columna.Tipo);

            if (tipo is null)
            {
                problemas.Add(new($"{nombre}: elige un tipo de datos."));
                return problemas;
            }

            if (tipo.UsaLongitud && !columna.EsMax
                && (columna.Longitud is not int longitud || longitud < 1 || longitud > tipo.LongitudMaxima))
            {
                var alternativa = tipo.AdmiteMax ? ", o marca Máxima (MAX)" : "";
                problemas.Add(new($"{nombre}: la longitud de {tipo.Nombre} debe estar entre 1 y {tipo.LongitudMaxima}{alternativa}."));
            }

            if (tipo.UsaPrecision && !(tipo.PrecisionOpcional && columna.Precision is null && columna.Escala is null))
            {
                if (columna.Precision is not int precision || precision < 1 || precision > 38)
                {
                    problemas.Add(new($"{nombre}: la precisión debe estar entre 1 y 38."));
                }
                else if (columna.Escala is not int escala || escala < 0 || escala > precision)
                {
                    problemas.Add(new($"{nombre}: la escala debe estar entre 0 y la precisión, {precision}."));
                }
            }

            if (tipo.UsaFraccionSegundos && (columna.FraccionSegundos is not int fraccion || fraccion < 0 || fraccion > tipo.FraccionMaxima))
            {
                problemas.Add(new($"{nombre}: los decimales de segundo deben estar entre 0 y {tipo.FraccionMaxima}."));
            }

            if (columna.EsClavePrincipal && columna.PermiteNulos)
            {
                problemas.Add(new($"{nombre}: una columna de la clave principal (PRIMARY KEY) no puede admitir NULL. Desmarca Permitir NULL."));
            }

            if (columna.EsMax && (columna.EsClavePrincipal || columna.EsUnica))
            {
                problemas.Add(new($"{nombre}: un tipo de longitud máxima (MAX) no puede ser clave principal ni valor único, porque no se puede indexar."));
            }

            if (columna.Referencia is not null)
            {
                problemas.AddRange(ValidarReferencia(
                    nombre,
                    columna.TipoCompleto,
                    columna.PermiteNulos,
                    !string.IsNullOrWhiteSpace(columna.ValorPredeterminado),
                    columna.Referencia,
                    terminos));
            }

            if (columna.EsIdentidad)
            {
                if (!tipo.AdmiteIdentidad)
                {
                    problemas.Add(new($"{nombre}: solo los tipos enteros, como {terminos.EjemploTiposIdentidad}, pueden ser de identidad ({terminos.NombreIdentidad})."));
                }
                else if (columna.Escala is > 0)
                {
                    problemas.Add(new($"{nombre}: una columna de identidad ({terminos.NombreIdentidad}) no puede tener decimales; deja la escala en 0."));
                }

                if (columna.PermiteNulos)
                {
                    problemas.Add(new($"{nombre}: una columna de identidad ({terminos.NombreIdentidad}) no puede admitir NULL."));
                }

                if (!string.IsNullOrWhiteSpace(columna.ValorPredeterminado))
                {
                    problemas.Add(new($"{nombre}: una columna de identidad ({terminos.NombreIdentidad}) no puede tener valor predeterminado (DEFAULT)."));
                }

                if (columna.Incremento == 0)
                {
                    problemas.Add(new($"{nombre}: el incremento de la identidad no puede ser cero."));
                }
            }

            return problemas;
        }

        public List<ProblemaValidacion> ValidarReferencia(
            string columna,
            string tipoColumna,
            bool permiteNulos,
            bool tienePredeterminado,
            ReferenciaModelo referencia,
            TerminosMotorModelo? terminos = null)
        {
            terminos ??= CapacidadesMotorModelo.SqlServer.Terminos;
            var problemas = new List<ProblemaValidacion>();

            if (string.IsNullOrWhiteSpace(referencia.ColumnaDestino))
            {
                problemas.Add(new($"{columna}: elige la tabla y la columna a la que apunta (FOREIGN KEY)."));
                return problemas;
            }

            var destino = $"{referencia.TablaDestino.NombreCorto()}.{referencia.ColumnaDestino}";

            if (!terminos.TiposCompatibles(tipoColumna, referencia.TipoDestino))
            {
                problemas.Add(new($"{columna} es {tipoColumna} y {destino} es {referencia.TipoDestino}: los dos lados de una llave foránea deben tener el mismo tipo de datos."));
            }

            var reglas = new[] { referencia.AlEliminar, referencia.AlActualizar };

            if (reglas.Contains(ReglaReferencial.EstablecerNulo) && !permiteNulos)
            {
                problemas.Add(new($"{columna}: la regla Establecer en NULL (SET NULL) necesita que la columna admita NULL."));
            }

            if (reglas.Contains(ReglaReferencial.EstablecerPredeterminado) && !tienePredeterminado)
            {
                problemas.Add(new($"{columna}: la regla Establecer predeterminado (SET DEFAULT) necesita que la columna tenga un valor predeterminado."));
            }

            return problemas;
        }

        public List<ProblemaValidacion> ValidarTabla(DefinicionTablaModelo tabla, IReadOnlyList<TipoDatosModelo> tipos, TerminosMotorModelo? terminos = null)
        {
            terminos ??= CapacidadesMotorModelo.SqlServer.Terminos;
            var problemas = new List<ProblemaValidacion>();

            if (string.IsNullOrWhiteSpace(tabla.Nombre))
            {
                problemas.Add(new("Escribe el nombre de la tabla."));
            }

            if (string.IsNullOrWhiteSpace(tabla.Esquema))
            {
                problemas.Add(new("Escribe el esquema de la tabla."));
            }

            if (tabla.Columnas.Count == 0)
            {
                problemas.Add(new("Agrega al menos una columna."));
                return problemas;
            }

            for (var i = 0; i < tabla.Columnas.Count; i++)
            {
                var otras = tabla.Columnas.Where((_, indice) => indice != i);
                problemas.AddRange(ValidarColumna(tabla.Columnas[i], otras, tipos, terminos));
            }

            var identidades = tabla.Columnas.Where(c => c.EsIdentidad).Select(c => c.Nombre).ToList();

            if (identidades.Count > 1)
            {
                problemas.Add(new($"Solo puede haber una columna de identidad ({terminos.NombreIdentidad}) por tabla; ahora hay {identidades.Count}: {NarradorTablaService.Unir(identidades)}."));
            }

            if (!tabla.Columnas.Any(c => c.EsClavePrincipal))
            {
                problemas.Add(new("La tabla no tiene clave principal (PRIMARY KEY). Se puede crear así, pero se recomienda una para identificar cada fila.", true));
            }

            return problemas.DistinctBy(p => p.Mensaje).ToList();
        }
    }
}

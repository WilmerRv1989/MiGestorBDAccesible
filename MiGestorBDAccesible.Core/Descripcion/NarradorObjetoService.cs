using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;

namespace MiGestorBDAccesible.Core.Descripcion
{
    public class NarradorObjetoService
    {
        public DescripcionTablaModelo Describir(DetalleObjetoModelo detalle, TipoMotor motor)
        {
            var objeto = detalle.Objeto;
            var resumen = new List<string>();
            var secciones = new List<SeccionDescripcionModelo>();

            switch (objeto.Tipo)
            {
                case TipoObjeto.Vista:
                    resumen.Add($"Vista con {Contar(detalle.Columnas.Count, "columna", "columnas")}: {NarradorTablaService.Unir(detalle.Columnas.Select(c => c.Nombre))}.");

                    if (objeto.Materializada)
                    {
                        resumen.Add("Es una vista materializada (MATERIALIZED VIEW): guarda el resultado en disco, así que hay que refrescarla para ver los datos nuevos.");
                    }

                    secciones.Add(SeccionColumnas(detalle));
                    break;

                case TipoObjeto.Procedimiento:
                case TipoObjeto.Funcion:
                    resumen.Add(ResumenRutina(detalle));
                    secciones.Add(new()
                    {
                        Titulo = "Parámetros",
                        Frases = detalle.Parametros.Count > 0
                            ? detalle.Parametros.Select(p => $"{p.Nombre}: {p.TipoDatos}, {Direccion(p.Direccion, motor)}.").ToList()
                            : ["No tiene parámetros."]
                    });

                    if (detalle.DevuelveTabla && detalle.Columnas.Count > 0)
                    {
                        secciones.Add(SeccionColumnas(detalle, "Columnas que devuelve"));
                    }

                    break;

                case TipoObjeto.Paquete:
                    resumen.Add(detalle.Subprogramas.Count > 0
                        ? $"Paquete con {Contar(detalle.Subprogramas.Count, "subprograma público", "subprogramas públicos")}: {NarradorTablaService.Unir(detalle.Subprogramas)}."
                        : "Paquete sin subprogramas públicos.");
                    break;

                case TipoObjeto.Disparador:
                    resumen.Add(ResumenDisparador(detalle, motor));
                    break;

                case TipoObjeto.Secuencia:
                    if (detalle.Retorno is not null)
                    {
                        resumen.Add($"Secuencia de tipo {detalle.Retorno}.");
                    }

                    secciones.Add(new() { Titulo = "Valores", Frases = FrasesSecuencia(detalle) });
                    break;
            }

            if (motor == TipoMotor.Oracle && objeto.Tipo != TipoObjeto.Secuencia)
            {
                resumen.Add(objeto.Valido
                    ? "Está válido (VALID): compiló sin errores."
                    : $"Está inválido (INVALID): {(detalle.Errores.Count > 0 ? $"tiene {Contar(detalle.Errores.Count, "error", "errores")} de compilación" : "hay que volver a compilarlo")}.");
            }

            if (detalle.Errores.Count > 0)
            {
                secciones.Add(new()
                {
                    Titulo = "Errores de compilación",
                    Frases = detalle.Errores.Select(e => $"Línea {e.Linea}, columna {e.Columna}: {e.Texto}").ToList()
                });
            }

            if (detalle.Notas.Count > 0)
            {
                secciones.Add(new() { Titulo = "Notas", Frases = detalle.Notas });
            }

            return new DescripcionTablaModelo
            {
                Titulo = $"Descripción {objeto.Tipo.DeArticulo()} {objeto.NombreCompleto}",
                Resumen = resumen,
                Secciones = secciones
            };
        }

        public static string Direccion(DireccionParametro direccion, TipoMotor motor) => (direccion, motor) switch
        {
            (DireccionParametro.Salida, TipoMotor.SqlServer) => "de salida (OUTPUT)",
            (DireccionParametro.Salida, _) => "de salida (OUT)",
            (DireccionParametro.EntradaSalida, _) => "de entrada y salida (IN OUT)",
            _ => "de entrada"
        };

        public static bool EsFuncionDisparador(DetalleObjetoModelo detalle) =>
            detalle.Retorno is "trigger" or "event_trigger";

        private static string ResumenRutina(DetalleObjetoModelo detalle)
        {
            var parametros = detalle.Parametros.Count == 0 ? "sin parámetros" : $"con {Contar(detalle.Parametros.Count, "parámetro", "parámetros")}";

            if (detalle.Objeto.Tipo == TipoObjeto.Procedimiento)
            {
                return $"Procedimiento {parametros}.";
            }

            if (EsFuncionDisparador(detalle))
            {
                return $"Función {parametros} que devuelve {detalle.Retorno}: es la que ejecuta un disparador, no se llama directamente.";
            }

            var retorno = detalle.DevuelveTabla
                ? detalle.Columnas.Count > 0
                    ? $"una tabla con {Contar(detalle.Columnas.Count, "columna", "columnas")}: {NarradorTablaService.Unir(detalle.Columnas.Select(c => c.Nombre))}"
                    : "varias filas"
                : detalle.Retorno ?? "un valor";

            return $"Función {parametros} que devuelve {retorno}.";
        }

        private static string ResumenDisparador(DetalleObjetoModelo detalle, TipoMotor motor)
        {
            var eventos = UnirO(detalle.Eventos);
            var frase = detalle.Momento switch
            {
                "BEFORE" => $"Se ejecuta antes de cada {eventos} (BEFORE)",
                "AFTER" => $"Se ejecuta después de cada {eventos} (AFTER)",
                "INSTEAD OF" => $"Se ejecuta en lugar de cada {eventos} (INSTEAD OF)",
                "COMPOUND" => $"Es un disparador compuesto (COMPOUND) para {eventos}",
                _ => $"Se ejecuta en el evento {eventos}"
            };

            if (detalle.PorFila is { } porFila)
            {
                frase += porFila ? ", fila por fila (FOR EACH ROW)" : ", una vez por instrucción";
            }

            if (detalle.Objeto.Tabla is { } tabla)
            {
                frase += $", sobre la tabla {tabla.NombreCorto()}";
            }
            else
            {
                frase += motor == TipoMotor.Oracle ? ", en todo el esquema" : ", en toda la base de datos";
            }

            if (detalle.Etiquetas.Count > 0)
            {
                frase += $", solo para {UnirO(detalle.Etiquetas)}";
            }

            if (detalle.FuncionDisparador is not null)
            {
                frase += $"; llama a la función {detalle.FuncionDisparador}";
            }

            return $"{frase}; está {(detalle.Habilitado == false ? "deshabilitado" : "habilitado")}.";
        }

        private static List<string> FrasesSecuencia(DetalleObjetoModelo detalle)
        {
            var frases = new List<string>();

            if (detalle.ValorActual is not null)
            {
                frases.Add($"Último valor entregado: {detalle.ValorActual}.");
            }
            else if (detalle.Inicio is not null)
            {
                frases.Add($"Todavía no se ha usado; el primer valor será {detalle.Inicio}.");
            }

            if (detalle.Inicio is not null)
            {
                frases.Add($"Empieza en {detalle.Inicio} (START WITH).");
            }

            frases.Add($"Sube de {detalle.Incremento} en {detalle.Incremento} (INCREMENT BY).");
            frases.Add($"Valor mínimo: {detalle.Minimo} (MINVALUE).");
            frases.Add($"Valor máximo: {detalle.Maximo} (MAXVALUE).");
            frases.Add(detalle.Ciclo == true
                ? "Al llegar al máximo vuelve a empezar desde el mínimo (CYCLE)."
                : "Al llegar al máximo da error en vez de repetir números (NO CYCLE).");

            return frases;
        }

        private static SeccionDescripcionModelo SeccionColumnas(DetalleObjetoModelo detalle, string titulo = "Columnas") => new()
        {
            Titulo = titulo,
            Frases = detalle.Columnas.Select(c => $"{c.Nombre}: {c.TipoDatos}, {(c.PermiteNulos ? "opcional (NULL)" : "obligatoria (NOT NULL)")}.").ToList()
        };

        private static string UnirO(IEnumerable<string> elementos)
        {
            var lista = elementos.ToList();

            return lista.Count switch
            {
                0 => "",
                1 => lista[0],
                _ => $"{string.Join(", ", lista.Take(lista.Count - 1))} o {lista[^1]}"
            };
        }

        private static string Contar(long cantidad, string singular, string plural) =>
            cantidad == 1 ? $"1 {singular}" : $"{cantidad:N0} {plural}";
    }
}

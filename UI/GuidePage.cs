namespace PhykroTrainer.UI;

public sealed class GuidePage : TrainerPage
{
    public GuidePage(MainForm host) : base(host)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var header = Ui.Section("GUÍA RÁPIDA");

        var text = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            Font = Theme.Ui,
            ReadOnly = true,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Text = GuideText
        };

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(text, 0, 1);
        Controls.Add(root);
    }

    private const string GuideText =
        "CÓMO ENCONTRAR UNA DIRECCIÓN (EJEMPLO: RUPIAS)\n\n" +
        "1. Abre el juego y conecta el trainer.\n" +
        "2. En el juego, apunta el valor exacto que quieres cambiar (por ejemplo, 42 rupias).\n" +
        "3. Ve a Escáner. Tipo: Int32. Comparación: Valor exacto. Valor: 42.\n" +
        "4. Pulsa Buscar. Si salen muchísimos resultados, no pasa nada.\n" +
        "5. Vuelve al juego, gasta o gana rupias (por ejemplo, hasta 50).\n" +
        "6. En el trainer elige «Valor exacto = 50» o «Aumentó» y pulsa Buscar (ahora filtra).\n" +
        "7. Repite el paso 5–6 hasta que queden 1–5 direcciones.\n" +
        "8. Selecciona las filas y pulsa «Añadir a trucos». Ponle nombre, por ejemplo «Rupias».\n" +
        "9. En Trucos escribe el valor que quieras (9999), marca «Congelar» y listo.\n\n" +
        "CONSEJOS PARA ACERTAR EL TIPO\n\n" +
        "• Dinero, vida, munición, contadores: casi siempre Int32 (4 bytes).\n" +
        "• Posiciones, velocidades, ángulos: casi siempre Float (4 bytes).\n" +
        "• Si un valor no aparece con Int32, prueba Int16 o Float.\n" +
        "• Si el número que ves es una barra (corazones, energía), el valor interno puede ser\n" +
        "  distinto. Busca un número y ve probando; si al congelarlo no cambia nada, descártalo.\n\n" +
        "VIDA INFINITA SIN SABER EL VALOR EXACTO\n\n" +
        "1. Busca la vida con un valor exacto que veas.\n" +
        "2. Recibe daño y usa «Disminuyó».\n" +
        "3. Si no queda ninguna dirección, repite desde el principio con otro tipo.\n" +
        "4. Congela el valor en el máximo (el que tenía al empezar).\n\n" +
        "QUE LA DIRECCIÓN SIGA FUNCIONANDO AL REINICIAR\n\n" +
        "Las direcciones absolutas (0x...) pueden cambiar al cerrar el juego. Si la dirección cae dentro\n" +
        "del módulo, el trainer la guarda como ocarina.exe+0x... y suele seguir sirviendo.\n" +
        "Para valores del montón (heap), Cheat Engine puede darte una «pointer path»:\n" +
        "   modulo.exe+0x1A2B3C,0x10,0x8\n" +
        "Pega esa cadena en el campo Dirección del truco: el trainer la resuelve sola.\n\n" +
        "ATAJOS\n\n" +
        "• Cada truco puede tener su atajo (F1, Ctrl+Alt+2...). Al pulsarlo se activa o desactiva «Congelar».\n" +
        "• F9 guarda un waypoint en la posición actual.\n" +
        "• F10 teletransporta al waypoint seleccionado.\n" +
        "• Ctrl+Alt+T oculta o muestra esta ventana (útil con el juego en pantalla completa).\n" +
        "• Los atajos son globales: funcionan aunque el juego tenga el foco.\n\n" +
        "WAYPOINTS / TELETRANSPORTE\n\n" +
        "1. Encuentra tres floats que cambien al moverte: normalmente X, Y y Z, seguidos en memoria.\n" +
        "2. Copia sus direcciones en Waypoints (X, Y, Z) y pulsa «Guardar direcciones».\n" +
        "3. Muévete a un sitio, ponle nombre y pulsa «Guardar posición» (o F9).\n" +
        "4. Selecciona el waypoint y pulsa «Teletransportar» (o F10).\n" +
        "Si el juego usa otra escala (por ejemplo, metros x100), el teletransporte puede dejarte\n" +
        "en otro sitio: ajusta a mano o busca las coordenadas que muestra el propio juego.\n\n" +
        "SEGURIDAD\n\n" +
        "• Haz copia de tus partidas antes de probar valores raros.\n" +
        "• Congelar un valor que el juego usa para otra cosa puede colgarlo. Si pasa, cierra el juego,\n" +
        "  abre el trainer y pulsa «Restaurar originales» (o simplemente reinicia el juego).\n" +
        "• No uses esto en juegos online: puede afectar a otros jugadores y conllevar expulsión.\n\n" +
        "SI NO CONECTA\n\n" +
        "• Abre el juego antes y pulsa Actualizar.\n" +
        "• Si el juego está elevado, pulsa Ajustes → Reiniciar como administrador.\n" +
        "• El detalle de cada error se guarda en trainer.log (Ajustes → Abrir carpeta de datos).\n";
}

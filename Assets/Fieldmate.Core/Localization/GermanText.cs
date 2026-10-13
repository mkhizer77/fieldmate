using System;
using System.Collections.Generic;

namespace Fieldmate.Localization;

/// <summary>
/// German for the short strings users see or hear (#24), keyed by the English text so the English stays readable at
/// the call site. Format strings keep their placeholders ({0}, {1}); GermanTextTests checks both sides match and that
/// every literal passed to Lang.T / Lang.F in the source has an entry. Informal "du", like the mate's English. Manual
/// content (parts, sections) is translated in manual.json instead.
/// </summary>
public static class GermanText
{
    public static readonly IReadOnlyDictionary<string, string> Table = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Procedure, steps and safety rules (DemoProcedures).
        ["Replace the relief valve cartridge"] = "Ventileinsatz des Überdruckventils tauschen",
        ["Inspect the relief valve"] = "Überdruckventil prüfen",
        ["Lock out the main breaker"] = "Hauptschalter abschließen",
        ["Close the inlet valve"] = "Einlassventil schließen",
        ["Verify zero pressure on the gauge"] = "Druckfreiheit am Manometer prüfen",
        ["Remove the pump access cover"] = "Pumpendeckel abnehmen",
        ["Fit the new relief cartridge"] = "Neuen Ventileinsatz einsetzen",
        ["Refit the pump access cover"] = "Pumpendeckel wieder aufsetzen",
        ["Open the inlet valve"] = "Einlassventil öffnen",
        ["Switch the main breaker back on"] = "Hauptschalter wieder einschalten",
        ["Verify running pressure is back in the green band"] = "Prüfen, ob der Betriebsdruck wieder im grünen Bereich ist",
        ["Lock out the breaker before touching the inlet valve."] = "Erst den Hauptschalter abschließen, dann das Einlassventil anfassen.",
        ["Lock out the breaker before opening the pump."] = "Erst den Hauptschalter abschließen, dann die Pumpe öffnen.",
        ["Close the inlet before opening the relief valve seat."] = "Erst das Einlassventil schließen, dann den Ventilsitz öffnen.",
        ["Refit the pump cover before switching the breaker."] = "Erst den Pumpendeckel wieder aufsetzen, dann den Hauptschalter schalten.",
        ["Refit the pump cover before opening the inlet, or water leaks out of the pump."] =
            "Erst den Pumpendeckel wieder aufsetzen, dann das Einlassventil öffnen, sonst läuft Wasser aus der Pumpe.",
        ["Open the inlet before switching the breaker, or the pump runs dry."] =
            "Erst das Einlassventil öffnen, dann den Hauptschalter schalten, sonst läuft die Pumpe trocken.",
        ["Lock out the breaker before closing the outlet valve; throttling it while the pump runs spikes the pressure."] =
            "Erst den Hauptschalter abschließen, dann das Auslassventil schließen. Bei laufender Pumpe schnellt der Druck sonst hoch.",
        ["Open the outlet valve before switching the breaker on; the pump must never run against a closed outlet."] =
            "Erst das Auslassventil öffnen, dann den Hauptschalter einschalten. Die Pumpe darf nie gegen ein geschlossenes Auslassventil laufen.",

        // Input words (InputWords): hands pinch, controllers grip/trigger.
        ["pinch"] = "greifen",
        ["grip"] = "Grifftaste",
        ["Press trigger on"] = "Drück mit dem Trigger auf",
        ["Pinch"] = "Drück auf",
        ["pull the trigger"] = "drück den Trigger",
        ["pinch with thumb and index finger"] = "führ Daumen und Zeigefinger zusammen",
        ["Hold X to talk"] = "X halten zum Sprechen",
        ["Pinch and hold your left middle finger to talk"] = "Links Mittelfinger und Daumen zusammen halten zum Sprechen",
        ["Point at the floor · trigger to place · thumbstick to rotate"] = "Auf den Boden zeigen · Trigger zum Abstellen · Stick zum Drehen",
        ["Point your right hand at the floor · pinch to place"] = "Mit der rechten Hand auf den Boden zeigen · greifen zum Abstellen",
        ["Y or Move machine"] = "Y oder Maschine versetzen",
        ["Move machine"] = "Maschine versetzen",
        ["Press the menu button on the left controller for the menu"] = "Menütaste am linken Controller drücken für das Menü",
        ["Turn your left palm towards you for the menu"] = "Linke Handfläche zu dir drehen für das Menü",
        ["point the right controller where you want me and pull the trigger"] =
            "zeig mit dem rechten Controller dorthin, wo du mich haben willst, und drück den Trigger",
        ["point your right hand where you want me and pinch"] =
            "zeig mit der rechten Hand dorthin, wo du mich haben willst, und führ Daumen und Zeigefinger zusammen",
        ["point the right controller at an open spot on the floor and pull the trigger"] =
            "zeig mit dem rechten Controller auf eine freie Stelle am Boden und drück den Trigger",
        ["point your right hand at an open spot on the floor and pinch"] =
            "zeig mit der rechten Hand auf eine freie Stelle am Boden und führ Daumen und Zeigefinger zusammen",

        // Part states, as the step card shows them.
        ["on"] = "ein",
        ["off"] = "aus",
        ["locked"] = "gesperrt",
        ["open"] = "offen",
        ["closed"] = "zu",
        ["removed"] = "abgenommen",
        ["fitted"] = "aufgesetzt",

        // Assistant panel (AssistantPanel): state line and detail card.
        ["Listening…"] = "Ich höre zu…",
        ["Transcribing…"] = "Ich schreibe mit…",
        ["Thinking…"] = "Ich denke nach…",
        ["From the manual"] = "Aus dem Handbuch",
        ["Procedure"] = "Ablauf",
        ["Step {0} of {1}: {2}"] = "Schritt {0} von {1}: {2}",
        ["Maintenance log"] = "Wartungsprotokoll",

        // Voice loop (VoiceLoop): idle hint, banner, errors.
        ["Assistant offline"] = "Assistent offline",
        ["Offline: scripted answers from the manual until the service is back."] =
            "Offline: Antworten aus dem Handbuch, bis der Dienst wieder da ist.",
        ["No microphone available (or permission denied)."] = "Kein Mikrofon verfügbar (oder keine Berechtigung).",
        ["Something went wrong with the assistant."] = "Beim Assistenten ist etwas schiefgelaufen.",

        // Guided setup (SetupFlow).
        ["Setup · 1 of 4 · Room"] = "Einrichtung · 1 von 4 · Raum",
        ["Setup · 2 of 4 · Your mate"] = "Einrichtung · 2 von 4 · Dein Begleiter",
        ["Point where you want Fieldmate to stay, then {0}."] = "Zeig dorthin, wo Fieldmate bleiben soll, und {0}.",
        ["Setup · 3 of 4 · Machine"] = "Einrichtung · 3 von 4 · Maschine",
        ["Setup · 4 of 4 · Today"] = "Einrichtung · 4 von 4 · Heute",
        ["Menu"] = "Menü",
        ["{0}: Start, Move machine, Occlusion, Stats, Labels, Camera, Language."] =
            "{0}: Start, Maschine versetzen, Verdeckung, Statistik, Beschriftungen, Kamera, Sprache.",

        // Procedure card (ProcedureDirector, ProcedurePanel).
        ["Both hands on. Turn them together."] = "Beide Hände dran. Jetzt gemeinsam drehen.",
        ["{0} steps. {1} {2}, or ask Fieldmate to start the procedure."] =
            "{0} Schritte. {1} {2} oder bitte Fieldmate, den Ablauf zu starten.",
        ["Ready"] = "Bereit",
        ["Step 0: Place the machine"] = "Schritt 0: Maschine aufstellen",
        ["It is where you left it. {0} {1} to continue, or {2} to place it again."] =
            "Sie steht noch da, wo du sie gelassen hast. {0} {1} zum Weitermachen oder {2} zum neu Aufstellen.",
        ["Setup"] = "Einrichtung",
        ["Before you start"] = "Vor dem Start",
        [". Leave room to walk around it."] = ". Lass genug Platz, um herumzugehen.",
        ["Looking for where you left it…"] = "Ich suche, wo du sie abgestellt hast…",
        ["Press again to restart the procedure from step 1."] = "Noch mal drücken, um den Ablauf ab Schritt 1 neu zu starten.",
        ["Reading the gauge…"] = "Ich lese das Manometer ab…",
        ["Safety: {0}"] = "Sicherheit: {0}",
        ["Step {0} of {1}"] = "Schritt {0} von {1}",
        ["Debrief"] = "Auswertung",
        ["Passed"] = "Bestanden",
        ["Not passed"] = "Nicht bestanden",
        ["Well done."] = "Gut gemacht.",
        ["Review the notes above and run it again."] = "Sieh dir die Hinweise oben an und mach es noch mal.",
        ["Time"] = "Zeit",
        ["Errors"] = "Fehler",
        ["Safety"] = "Sicherheit",
        ["Help"] = "Hilfe",

        // Gauge reading (GaugeCheck).
        ["Gauge reads {0} {1}, not {2} yet. Keep watching or check the isolation."] =
            "Manometer zeigt {0} {1}, noch nicht {2}. Schau weiter hin oder prüf die Ventile.",
        ["Gauge reads {0} {1}, below {2}. Is the inlet open and the breaker on?"] =
            "Manometer zeigt {0} {1}, unter {2}. Ist das Einlassventil offen und der Hauptschalter ein?",

        // How-to tips (InteractionTips).
        ["One controller on the bar. Hold the grip on the other end with the second controller, then turn together."] =
            "Ein Controller ist am Griff. Halt mit dem zweiten Controller die Grifftaste am anderen Ende, dann gemeinsam drehen.",
        ["One hand on the bar. Pinch the other end with your other hand, keep both pinched, and turn together."] =
            "Eine Hand ist am Griff. Greif mit der anderen Hand das andere Ende, halt beide fest und dreh gemeinsam.",
        ["Tip: just look at the highlighted part and hold your gaze for a moment."] =
            "Tipp: Schau einfach einen Moment lang auf das markierte Teil.",
        ["Tip: put the controller on the handle, hold the grip, and move it around the pivot. Let go to settle."] =
            "Tipp: Controller an den Griff, Grifftaste halten und um den Drehpunkt bewegen. Loslassen zum Einrasten.",
        ["Tip: put your hand on the handle, pinch thumb and index, keep pinching and move your hand around the pivot. Let go to settle."] =
            "Tipp: Hand an den Griff, Daumen und Zeigefinger zusammen, festhalten und um den Drehpunkt bewegen. Loslassen zum Einrasten.",
        ["Tip: both controllers on the bar, hold both grips, and turn them together like a steering wheel."] =
            "Tipp: Beide Controller an den Griff, beide Grifftasten halten und gemeinsam drehen wie ein Lenkrad.",
        ["Tip: both hands on the bar, pinch with each, keep both pinched and turn them together like a steering wheel."] =
            "Tipp: Beide Hände an den Griff, mit jeder greifen, festhalten und gemeinsam drehen wie ein Lenkrad.",
        ["Tip: hold the grip on the cover and pull it straight towards you, then let go."] =
            "Tipp: Grifftaste am Deckel halten, ihn gerade zu dir ziehen, dann loslassen.",
        ["Tip: pinch the cover, pull it straight towards you, then release the pinch."] =
            "Tipp: Deckel greifen, gerade zu dir ziehen, dann loslassen.",
        ["Tip: hold the grip on the cover, bring it back to the front of the pump and let go; close by, it clicks on."] =
            "Tipp: Grifftaste am Deckel halten, ihn zurück vor die Pumpe bringen und loslassen. Nah dran rastet er ein.",
        ["Tip: pinch the cover, bring it back to the front of the pump and release; close by, it clicks on."] =
            "Tipp: Deckel greifen, zurück vor die Pumpe bringen und loslassen. Nah dran rastet er ein.",
        ["Tip: hold the grip on the cartridge, carry it to the seat, let go when it snaps."] =
            "Tipp: Grifftaste am Ventileinsatz halten, zum Ventilsitz tragen und loslassen, wenn er einrastet.",
        ["Tip: pinch the cartridge on the tray, carry it to the seat, release when it snaps in."] =
            "Tipp: Ventileinsatz auf dem Tablett greifen, zum Ventilsitz tragen und loslassen, wenn er einrastet.",

        // Placement hint card (MachinePlacement).
        ["Place the machine"] = "Maschine aufstellen",
        ["Follow the headset's room setup so the machine can stand on your real floor."] =
            "Folge der Raumeinrichtung des Headsets, damit die Maschine auf deinem echten Boden steht.",
        ["You can place it once the scan is done."] = "Sobald der Scan fertig ist, kannst du sie aufstellen.",
        ["Size {0} % · tight here, try a more open spot"] = "Größe {0} % · eng hier, versuch eine freiere Stelle",
        ["Full size: it fits here"] = "Volle Größe: passt hier",
        ["Size {0} % to fit the space"] = "{0} % Größe, damit sie in den Raum passt",
        [" · thumbstick up/down to resize"] = " · Stick hoch/runter für die Größe",

        // Vision (CaptureIndicator, LabelAnchor).
        ["Camera"] = "Kamera",
        ["Model data"] = "Modelldaten",
        ["Not sure"] = "Unsicher",

        // Buttons (PressButton labels are English keys: ProcedureDirector, HandMenu, PerfOverlay, OcclusionSettings,
        // PrivacySettings, MoveMachineButton). "Move machine" is under input words.
        ["Start"] = "Start",
        ["Keep here"] = "Hier lassen",
        ["Place first"] = "Erst aufstellen",
        ["Confirm restart"] = "Neustart bestätigen",
        ["Restart"] = "Neustart",
        ["Run again"] = "Nochmal starten",
        ["Stats"] = "Statistik",
        ["Stats: On"] = "Statistik: An",
        ["Labels: On"] = "Beschriftungen: An",
        ["Labels: Off"] = "Beschriftungen: Aus",
        ["Fit controllers"] = "Controller anpassen",
        ["Fit controllers: On"] = "Controller anpassen: An",
        ["Camera: On"] = "Kamera: An",
        ["Camera: Off (local-only)"] = "Kamera: Aus (nur lokal)",
        ["Occlusion: Hard"] = "Verdeckung: Hart",
        ["Occlusion: Soft"] = "Verdeckung: Weich",
        ["Occlusion: Off"] = "Verdeckung: Aus",
        ["Language: English"] = "Sprache: Deutsch",

        // Labels on the machine (ControlTag): control titles and purposes from the scene builder, part names from the
        // manual (same German as manual.json).
        ["Pump cover"] = "Pumpendeckel",
        ["opens the relief seat"] = "gibt den Ventilsitz frei",
        ["Inlet valve"] = "Einlassventil",
        ["pump suction"] = "Saugseite der Pumpe",
        ["Outlet valve"] = "Auslassventil",
        ["discharge"] = "Druckseite",
        ["Main breaker"] = "Hauptschalter",
        ["pump motor power"] = "Strom für den Pumpenmotor",
        ["New relief cartridge"] = "Neuer Ventileinsatz",
        ["fits the relief seat"] = "passt in den Ventilsitz",
        ["Electrical cabinet"] = "Schaltschrank",
        ["Drive motor"] = "Antriebsmotor",
        ["Motor mount"] = "Motorhalterung",
        ["Motor cooling fins"] = "Kühlrippen",
        ["Centrifugal pump"] = "Kreiselpumpe",
        ["Pump access cover"] = "Pumpendeckel",
        ["Pressure gauge"] = "Manometer",
        ["Discharge line"] = "Druckleitung",
        ["Pressure relief valve"] = "Überdruckventil",
        ["Relief valve seat"] = "Ventilsitz",
        ["Relief valve cartridge"] = "Ventileinsatz",
    };
}

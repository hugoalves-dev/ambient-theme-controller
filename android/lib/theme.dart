import 'package:flutter/material.dart';

import 'session_state.dart';

/// Brand palette shared with the Windows app (docs/brand/logo.svg).
abstract final class Brand {
  static const amber = Color(0xFFF5A524);
  static const amberSoft = Color(0xFFFFD27A);
  static const amberDeep = Color(0xFFC98314);
  static const night = Color(0xFF141821);
  static const nightCard = Color(0xFF1E2430);
  static const nightBorder = Color(0xFF2A3140);
  static const paper = Color(0xFFF6F3EC);
  static const paperBorder = Color(0xFFE8E1D3);
  static const ok = Color(0xFF3FB97F);
  static const waiting = Color(0xFF5B9BF0);
  static const problem = Color(0xFFE5534B);
}

Color toneColor(Tone tone) => switch (tone) {
  Tone.ok => Brand.ok,
  Tone.waiting => Brand.waiting,
  Tone.paused => Brand.amberDeep,
  Tone.problem => Brand.problem,
};

ThemeData buildTheme(Brightness brightness) {
  final dark = brightness == Brightness.dark;
  final background = dark ? Brand.night : Brand.paper;
  final card = dark ? Brand.nightCard : Colors.white;
  final border = dark ? Brand.nightBorder : Brand.paperBorder;
  final scheme = ColorScheme.fromSeed(seedColor: Brand.amber, brightness: brightness).copyWith(
    primary: Brand.amber,
    onPrimary: Brand.night,
    surface: background,
    surfaceContainerLow: card,
    outlineVariant: border,
  );
  final rounded = RoundedRectangleBorder(borderRadius: BorderRadius.circular(14));

  return ThemeData(
    colorScheme: scheme,
    scaffoldBackgroundColor: background,
    cardTheme: CardThemeData(
      color: card,
      elevation: 0,
      margin: const EdgeInsets.only(bottom: 14),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20), side: BorderSide(color: border)),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        minimumSize: const Size.fromHeight(52),
        shape: rounded,
        textStyle: const TextStyle(fontWeight: FontWeight.w600, fontSize: 16),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(shape: rounded, side: BorderSide(color: border)),
    ),
    inputDecorationTheme: InputDecorationTheme(
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(14)),
    ),
    snackBarTheme: const SnackBarThemeData(behavior: SnackBarBehavior.floating),
    dividerTheme: DividerThemeData(color: border),
    expansionTileTheme: const ExpansionTileThemeData(shape: Border(), collapsedShape: Border()),
  );
}

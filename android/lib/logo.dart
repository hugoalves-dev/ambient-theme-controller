import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'theme.dart';

/// The sun-and-moon logo drawn as vectors (same 120x120 geometry as docs/brand/logo.svg).
/// When [animate] is true the rays turn slowly, signalling a live sensor session.
class SunMoonLogo extends StatefulWidget {
  const SunMoonLogo({super.key, this.size = 40, this.animate = false, this.moonColor = Brand.night});

  final double size;
  final bool animate;
  final Color moonColor;

  @override
  State<SunMoonLogo> createState() => _SunMoonLogoState();
}

class _SunMoonLogoState extends State<SunMoonLogo> with SingleTickerProviderStateMixin {
  late final AnimationController _turn = AnimationController(vsync: this, duration: const Duration(seconds: 12));

  @override
  void initState() {
    super.initState();
    _sync();
  }

  @override
  void didUpdateWidget(SunMoonLogo oldWidget) {
    super.didUpdateWidget(oldWidget);
    _sync();
  }

  void _sync() => widget.animate ? _turn.repeat() : _turn.stop();

  @override
  void dispose() {
    _turn.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => SizedBox.square(
    dimension: widget.size,
    child: AnimatedBuilder(
      animation: _turn,
      builder: (context, _) => CustomPaint(painter: _LogoPainter(_turn.value * 2 * math.pi, widget.moonColor)),
    ),
  );
}

class _LogoPainter extends CustomPainter {
  _LogoPainter(this.rayAngle, this.moonColor);

  final double rayAngle;
  final Color moonColor;

  @override
  void paint(Canvas canvas, Size size) {
    canvas.scale(size.width / 120, size.height / 120);

    final rays = Paint()..color = Brand.amber;
    canvas.save();
    canvas.translate(60, 60);
    canvas.rotate(rayAngle);
    for (var i = 0; i < 8; i++) {
      canvas.drawRRect(RRect.fromLTRBR(-4, -56, 4, -40, const Radius.circular(4)), rays);
      canvas.rotate(math.pi / 4);
    }
    canvas.restore();

    final sun = Paint()
      ..shader = const RadialGradient(
        center: Alignment(-0.24, -0.3),
        radius: 0.75,
        colors: [Brand.amberSoft, Brand.amber],
      ).createShader(Rect.fromCircle(center: const Offset(60, 60), radius: 30));
    canvas.drawCircle(const Offset(60, 60), 30, sun);
    canvas.drawCircle(const Offset(76, 48), 24, Paint()..color = moonColor);
  }

  @override
  bool shouldRepaint(_LogoPainter oldDelegate) => oldDelegate.rayAngle != rayAngle || oldDelegate.moonColor != moonColor;
}

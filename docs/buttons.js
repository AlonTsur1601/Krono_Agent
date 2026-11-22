const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
const easing = 'cubic-bezier(.42, 0, .58, 1)';

for (const button of document.querySelectorAll('.button')) {
  const face = button.querySelector('span');
  let pulse;
  function press() {
    if (reducedMotion.matches) return;
    const current = getComputedStyle(face).transform;
    pulse?.cancel();
    pulse = face.animate([
      { transform: current, easing },
      { transform: 'translateY(4px)', offset: .44, easing },
      { transform: 'translateY(0)' },
    ], 200);
  }
  button.addEventListener('pointerdown', event => {
    if (event.isPrimary && event.button === 0) press();
  });
  button.addEventListener('keydown', event => {
    if (event.key === 'Enter' && !event.repeat) press();
  });
}

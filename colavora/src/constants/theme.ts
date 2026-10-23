// Brand palette ported verbatim from the original ColaVora app (constants/Colors.ts).
// Primary brand color is the navy #03204B; backgrounds are white. Keeping the same
// structure (default/contrast/shade/tint) so every screen matches the old design.

export const palette = {
  text: { default: '#000000', contrast: '#ffffff', shade: '#000000', tint: '#1a1a1a' },
  danger: { default: '#ff0000', contrast: '#000000', shade: '#e00000', tint: '#ff0000' },
  primary: { default: '#03204B', contrast: '#ffffff', shade: '#031c42', tint: '#1c365d' },
  background: { default: '#ffffff', contrast: '#000000', shade: '#e0e0e0', tint: '#ffffff' },
  backgroundSecondary: { default: '#FAFBFC', contrast: '#000000', shade: '#dcddde', tint: '#fbfbfc' },
  gray: { default: '#D9D9D9', contrast: '#000000', shade: '#bfbfbf', tint: '#dddddd' },
  noStatus: { default: '#5B5B5B', contrast: '#ffffff', shade: '#505050', tint: '#6b6b6b' },
  lightGray: { default: '#C2C2C240', contrast: '#000000', shade: '#abab38', tint: '#c8c853' },
} as const;

// Flat aliases for convenience.
export const colors = {
  primary: palette.primary.default, // #03204B
  primaryContrast: palette.primary.contrast, // #fff
  text: palette.text.tint,
  background: palette.background.default,
  backgroundSecondary: palette.backgroundSecondary.default,
  gray: palette.gray.default,
  danger: palette.danger.default,
  muted: palette.noStatus.default,
};

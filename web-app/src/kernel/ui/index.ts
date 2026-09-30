// Kit de componentes (src/kernel/ui). Contrato y props en web-app/KIT.md; patrones completos en ./templates.
export { IconEdit, IconKey, IconLogOut, IconPower, IconRefreshCw, IconRotateCcw, IconShield, IconTrash } from './actionIcons'
export { BrandLockup, BrandMark } from './Brand'
export type { BrandLockupProps, BrandMarkProps } from './Brand'
export { BRAND_SYMBOL_SRC, brandLockupSrc } from './brandAssets'
export { ClientPicker, ClientPickerInput } from './ClientPicker'
export type { ClientOption, ClientPickerInputProps, ClientPickerProps } from './ClientPicker'
export { CategoryProductPicker } from './CategoryProductPicker'
export type { CategoryOption, CategoryProductPickerProps, ProductSearchOption } from './CategoryProductPicker'
export { categoryLabel, categoryProductTotals, categoryTree, filterCategoryTree, isCategoryProductValue, sameCategoryProduct } from './categoryTree'
export type { CategoryNode, CategoryProductValue, CategoryTreeRow } from './categoryTree'
export { ComboSelect, ComboSelectInput } from './ComboSelect'
export type { ComboSelectInputProps, ComboSelectProps } from './ComboSelect'
export { exactComboMatch } from './comboMatch'
export type { ComboOption } from './comboMatch'
export { CommandPalette } from './CommandPalette'
export type { CommandPaletteProps } from './CommandPalette'
export {
  closeCommandPalette,
  filterCommands,
  groupCommands,
  isCommandPaletteOpen,
  openCommandPalette,
  useCommandPaletteOpen,
  useCommandPaletteShortcut,
} from './commandPaletteStore'
export type { CommandItem } from './commandPaletteStore'
export { Chip } from './Chip'
export type { ChipProps, ChipTone } from './Chip'
export { ConfirmDialog } from './ConfirmDialog'
export type { ConfirmDialogProps } from './ConfirmDialog'
export { DataTable } from './DataTable'
export type { DataColumn, DataTableProps, RowAction, SortState, SortValue } from './DataTable'
export { EMPTY_RANGE, inDateRange } from './dateRange'
export type { DateRange } from './dateRange'
export { EmptyState } from './EmptyState'
export type { EmptyStateProps } from './EmptyState'
export { DateRangeFilter, Filters, SelectFilter } from './Filters'
export type { DateRangeFilterProps, FilterOption, FiltersProps, SelectFilterProps } from './Filters'
export { DateInput, Field, Form, NumberInput, Select, TextArea, TextInput, Toggle } from './Form'
export type { FieldProps, FormProps, SelectOption, SelectProps, TextInputProps, ToggleProps } from './Form'
export { matchesQ, normalizeQ } from './matchesQ'
export { Modal } from './Modal'
export type { ModalProps } from './Modal'
export { Panel } from './Panel'
export type { PanelProps } from './Panel'
export { QBox } from './QBox'
// 'check' de la maqueta (el mismo del aviso de éxito): estados vacíos "todo resuelto".
export { IconCheck } from './icons'
export {
  IconBasket,
  IconBox,
  IconCart,
  IconCash,
  IconChart,
  IconCheckin,
  IconClip,
  IconClock,
  IconDoc,
  IconGear,
  IconGrid,
  IconLayers,
  IconLock,
  IconPencil,
  IconSwap,
  IconTag,
  IconUsers,
  IconWarehouse,
} from './screenIcons'
export type { QBoxProps } from './QBox'
export { SearchMultiSelect, SearchSelect } from './SearchSelect'
export type { SearchMultiSelectProps, SearchSelectProps } from './SearchSelect'
export { Spinner } from './Spinner'
export type { SpinnerProps } from './Spinner'
export { Tabs } from './Tabs'
export type { TabItem, TabsProps } from './Tabs'
export { getTheme, initTheme, setTheme, THEME_STORAGE_KEY, useTheme } from './theme'
export type { Theme } from './theme'
export { toast } from './toast'
export { CARDS_QUERY, useMediaQuery } from './useMediaQuery'

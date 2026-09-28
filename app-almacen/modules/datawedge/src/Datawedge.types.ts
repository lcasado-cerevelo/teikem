export type ScanEvent = {
  data: string
  symbology: string
}

export type DatawedgeModuleEvents = {
  onScan: (event: ScanEvent) => void
}

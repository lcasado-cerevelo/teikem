// Datos de «Impuestos» (Lote 56; permiso tax.manage, módulo TAX): tasas, reglas, exenciones de clientes y el simulador de cálculo.
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']
export type TaxRateDto = Schemas['TaxRateDto']
export type TaxRateRequest = Schemas['TaxRateRequest']
export type TaxComponentDto = Schemas['TaxComponentDto']
export type TaxSettingsDto = Schemas['TaxSettingsDto']
export type TaxSettingsRequest = Schemas['TaxSettingsRequest']
export type TaxExemptionDto = Schemas['TaxExemptionDto']
export type TaxExemptionRequest = Schemas['TaxExemptionRequest']
export type TaxExemptionPatchRequest = Schemas['TaxExemptionPatchRequest']
export type TaxCalcRequest = Schemas['TaxCalcRequest']
export type TaxCalcDto = Schemas['TaxCalcDto']
export type ProductTaxCategoryDto = Schemas['ProductTaxCategoryDto']

export const taxKeys = {
  all: ['tax'] as const,
  rates: ['tax', '/api/v1/tax/rates'] as const,
  settings: ['tax', '/api/v1/tax/settings'] as const,
  exemptions: ['tax', '/api/v1/tax/exemptions'] as const,
  products: (q: string, category: string) => ['tax', '/api/v1/tax/products', q, category] as const,
}

export function useTaxRates() {
  return useQuery({ queryKey: taxKeys.rates, queryFn: () => unwrap(api.GET('/api/v1/tax/rates')) })
}

export function useTaxSettings() {
  return useQuery({ queryKey: taxKeys.settings, queryFn: () => unwrap(api.GET('/api/v1/tax/settings')) })
}

export function useTaxExemptions() {
  return useQuery({ queryKey: taxKeys.exemptions, queryFn: () => unwrap(api.GET('/api/v1/tax/exemptions')) })
}

/** Tope de productos que trae el listado (el API pone 500): se afina con el buscador o con el filtro de categoría. */
export const PRODUCTS_TAKE = 500

export function useTaxProducts(q: string, category: string) {
  return useQuery({
    queryKey: taxKeys.products(q, category),
    queryFn: () => unwrap(api.GET('/api/v1/tax/products', { params: { query: { q: q || undefined, category: category || undefined, take: PRODUCTS_TAKE } } })),
    placeholderData: keepPreviousData,
  })
}

export function useTaxMutations() {
  const qc = useQueryClient()
  const refresh = () => qc.invalidateQueries({ queryKey: taxKeys.all })
  return {
    createRate: useMutation({ mutationFn: (body: TaxRateRequest) => unwrap(api.POST('/api/v1/tax/rates', { body })), onSettled: refresh }),
    closeRate: useMutation({
      mutationFn: (v: { id: number; effectiveTo: string }) => unwrap(api.POST('/api/v1/tax/rates/{id}/close', { params: { path: { id: v.id } }, body: { effectiveTo: v.effectiveTo } })),
      onSettled: refresh,
    }),
    saveSettings: useMutation({ mutationFn: (body: TaxSettingsRequest) => unwrap(api.PATCH('/api/v1/tax/settings', { body })), onSettled: refresh }),
    createExemption: useMutation({ mutationFn: (body: TaxExemptionRequest) => unwrap(api.POST('/api/v1/tax/exemptions', { body })), onSettled: refresh }),
    updateExemption: useMutation({
      mutationFn: (v: { id: number; body: TaxExemptionPatchRequest }) => unwrap(api.PATCH('/api/v1/tax/exemptions/{id}', { params: { path: { id: v.id } }, body: v.body })),
      onSettled: refresh,
    }),
    setExemptionActive: useMutation({
      mutationFn: (v: { id: number; active: boolean }) =>
        unwrap(v.active ? api.POST('/api/v1/tax/exemptions/{id}/reactivate', { params: { path: { id: v.id } } }) : api.POST('/api/v1/tax/exemptions/{id}/deactivate', { params: { path: { id: v.id } } })),
      onSettled: refresh,
    }),
    setProductCategory: useMutation({
      mutationFn: (v: { productId: number; category: string }) => unwrap(api.PUT('/api/v1/tax/products/{productId}/category', { params: { path: { productId: v.productId } }, body: { category: v.category } })),
      onSettled: refresh,
    }),
    calculate: useMutation({ mutationFn: (body: TaxCalcRequest) => unwrap(api.POST('/api/v1/tax/calculate', { body })) }),
  }
}

import SimpleCatalogPage from "../components/catalog/SimpleCatalogPage";
import { getBrands, createBrand, updateBrand, deactivateBrand } from "../api/catalog";

export default function BrandsPage() {
  return (
    <SimpleCatalogPage
      prefix="brand"
      titleKey="nav.brands"
      load={getBrands}
      create={createBrand}
      update={updateBrand}
      deactivate={deactivateBrand}
    />
  );
}

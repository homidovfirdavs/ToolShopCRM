import SimpleCatalogPage from "../components/catalog/SimpleCatalogPage";
import { getCategories, createCategory, updateCategory, deactivateCategory } from "../api/catalog";

export default function CategoriesPage() {
  return (
    <SimpleCatalogPage
      prefix="cat"
      titleKey="nav.categories"
      load={getCategories}
      create={createCategory}
      update={updateCategory}
      deactivate={deactivateCategory}
    />
  );
}

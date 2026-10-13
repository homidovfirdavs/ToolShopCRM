import SimpleCatalogPage from "../components/catalog/SimpleCatalogPage";
import { getCategories, createCategory, updateCategory, deactivateCategory } from "../api/catalog";
import { refreshCategoryNames } from "../lib/categoryNames";

export default function CategoriesPage() {
  return (
    <SimpleCatalogPage
      prefix="cat"
      titleKey="nav.categories"
      load={getCategories}
      translatable
      create={async (...a: Parameters<typeof createCategory>) => {
        const r = await createCategory(...a);
        void refreshCategoryNames(); // other pages show the new names without a reload
        return r;
      }}
      update={async (...a: Parameters<typeof updateCategory>) => {
        const r = await updateCategory(...a);
        void refreshCategoryNames();
        return r;
      }}
      deactivate={deactivateCategory}
    />
  );
}

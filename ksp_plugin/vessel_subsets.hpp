#pragma once

#include "base/disjoint_sets.hpp"
#include "ksp_plugin/pile_up.hpp"

// This ksp_plugin file is in namespace |base| to specialize a template declared
// therein.

namespace principia {
namespace base {

template<>
class Subset<ksp_plugin::Vessel>::Properties {
 public:
  class SubsetOfExistingPileUp {
   public:
    explicit SubsetOfExistingPileUp(not_null<ksp_plugin::PileUp*> pile_up);
   private:
    not_null<ksp_plugin::PileUp*> const pile_up_;
    int missing_;
    friend class Subset<ksp_plugin::Vessel>::Properties;
  };

  Properties(not_null<ksp_plugin::Vessel*> vessel,
             std::experimental::optional<SubsetOfExistingPileUp>
                 subset_of_existing_pile_up);

  void MergeWith(Properties& other);

 private:
  std::experimental::optional<SubsetOfExistingPileUp>
      subset_of_existing_pile_up_;
  std::list<std::list<not_null<ksp_plugin::Vessel*>>> vessels_;
};

}  // namespace base
}  // namespace principia

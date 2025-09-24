@partition @heal @reconcile
Feature: Partition buffering and later delivery; reconciling on owner

  Background:
    Given US owns Auction A which is "Active"
    And   EU has a mirrored Auction A which is "Active"

  Scenario: Bids during partition; heal; winner decided deterministically
    When inter-region link becomes Partitioned
    And  US places 310 on A
    And  EU places 300 on A
    And  both regions publish to their local buses

    When inter-region link becomes Connected
    And  both regions drain and apply buffered events

    Then no bids are lost
    And  US Reconcile(A) sets WinnerBidId to the US(310) bid
    And  US updates RECONCILIATION_CP for A

import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { fetchBooks } from 'Store/Actions/bookActions';
import { updateBookFiles } from 'Store/Actions/bookFileActions';
import ChangeBookModalContent from './ChangeBookModalContent';

function createMapStateToProps() {
  return createSelector(
    (state) => state.books,
    (state, { authorId }) => authorId,
    (books, authorId) => {
      const items = books.items
        .filter((book) => book.authorId === authorId)
        .sort((a, b) => a.title.localeCompare(b.title));

      return {
        items,
        isFetching: books.isFetching,
        isPopulated: books.isPopulated
      };
    }
  );
}

const mapDispatchToProps = {
  fetchBooks,
  updateBookFiles
};

class ChangeBookModalContentConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    // The author detail page already holds this author's books, but the book detail page
    // and a direct link do not, so only fetch when there is nothing to show.
    if (!this.props.items.length) {
      this.props.fetchBooks({ authorId: this.props.authorId });
    }
  }

  //
  // Listeners

  onBookSelect = (bookId, editionId) => {
    if (!editionId) {
      return;
    }

    this.props.updateBookFiles({
      bookFileIds: this.props.bookFileIds,
      editionId
    });

    this.props.onModalClose(true);
  };

  //
  // Render

  render() {
    const {
      items,
      isFetching,
      currentBookId,
      onModalClose
    } = this.props;

    return (
      <ChangeBookModalContent
        items={items}
        isFetching={isFetching}
        currentBookId={currentBookId}
        onBookSelect={this.onBookSelect}
        onModalClose={onModalClose}
      />
    );
  }
}

ChangeBookModalContentConnector.propTypes = {
  authorId: PropTypes.number.isRequired,
  bookFileIds: PropTypes.arrayOf(PropTypes.number).isRequired,
  currentBookId: PropTypes.number,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  isFetching: PropTypes.bool.isRequired,
  fetchBooks: PropTypes.func.isRequired,
  updateBookFiles: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(ChangeBookModalContentConnector);

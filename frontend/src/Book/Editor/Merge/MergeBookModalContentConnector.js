import _ from 'lodash';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { mergeBooks } from 'Store/Actions/bookIndexActions';
import MergeBookModalContent from './MergeBookModalContent';

function createMapStateToProps() {
  return createSelector(
    (state, { bookIds }) => bookIds,
    (state) => state.books.items,
    (state) => state.bookIndex,
    (bookIds, allBooks, bookIndex) => {
      const selectedBooks = _.intersectionWith(allBooks, bookIds, (b, id) => b.id === id);
      const books = _.orderBy(selectedBooks, 'title');

      return {
        books,
        isMerging: bookIndex.isMerging,
        mergeError: bookIndex.mergeError
      };
    }
  );
}

function createMapDispatchToProps(dispatch, props) {
  return {
    onMergeConfirmed(targetBookId) {
      const sourceBookIds = props.bookIds.filter((id) => id !== targetBookId);

      dispatch(mergeBooks({
        targetBookId,
        sourceBookIds
      }));

      props.onModalClose();
    }
  };
}

export default connect(createMapStateToProps, createMapDispatchToProps)(MergeBookModalContent);
